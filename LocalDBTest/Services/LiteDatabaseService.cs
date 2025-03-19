using LiteDB;
using System.IO;
using LocalDBTest.Models;
using System.Collections.Concurrent;

namespace LocalDBTest.Services;

public interface IDatabaseService
{
    LiteDatabase GetDatabase();
    Task SavePeopleWithRelationsAsync(IEnumerable<Person> people);
    Task<List<Person>> GetPeopleAsync();
    Task UpdatePeopleAsync(IEnumerable<Person> people);
    Task DeleteAllPeopleAsync();
    Task CleanDatabaseAsync();
    Task<double> GetDatabaseSizeInMb();
}

public class LiteDatabaseService : IDatabaseService, IDisposable
{
    private readonly string _dbPath;
    private LiteDatabase? _database;
    private readonly SemaphoreSlim _databaseLock = new(1, 1);
    private readonly ConcurrentDictionary<string, object> _collectionCache = new();
    // Static flag to avoid recreating indices multiple times
    private static bool _indicesCreated = false;
    private static readonly object _indicesLock = new();
    private readonly bool _cleanOnStartup;

    public LiteDatabaseService(bool cleanOnStartup = false)
    {
        _cleanOnStartup = cleanOnStartup;
        var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _dbPath = Path.Combine(path, "app.db");
        
        // Clean the database if requested
        if (_cleanOnStartup && File.Exists(_dbPath))
        {
            // Execute synchronously to ensure cleanup before first access
            CleanDatabaseAsync().GetAwaiter().GetResult();
        }
    }

    public LiteDatabase GetDatabase()
    {
        if (_database == null)
        {
            _databaseLock.Wait();
            try
            {
                _database ??= new LiteDatabase(_dbPath);
                
                // Create indices if not done already
                // Using lock to ensure thread safety
                if (!_indicesCreated)
                {
                    CreateIndices(
                        GetCollection<Person>("people"),
                        GetCollection<Address>("addresses"),
                        GetCollection<EmailAddress>("emails")
                    );
                }
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        return _database;
    }

    public async Task CleanDatabaseAsync()
    {
        await Task.Run(() =>
        {
            // Ensure we're thread-safe when cleaning the database
            _databaseLock.Wait();
            try
            {
                // Dispose database connection if exists
                _database?.Dispose();
                _database = null;
                
                // Clear collection cache to ensure fresh collections after cleanup
                _collectionCache.Clear();

                // Delete database file
                if (File.Exists(_dbPath))
                {
                    File.Delete(_dbPath);
                }
                
                // Reset indices flag
                _indicesCreated = false;
            }
            finally
            {
                _databaseLock.Release();
            }
        });
    }

    private LiteCollection<T> GetCollection<T>(string name) where T : class
    {
        // Use type-based cache key for better collection caching
        var cacheKey = $"{typeof(T).Name}_{name}";
        
        return (LiteCollection<T>)_collectionCache.GetOrAdd(cacheKey, _ => 
        {
            var db = GetDatabase();
            var collection = db.GetCollection<T>(name);
            return collection;
        });
    }

    public Task SavePeopleWithRelationsAsync(IEnumerable<Person> people)
    {
        return Task.Run(() =>
        {
            var peopleList = people.ToList();
            if (!peopleList.Any()) return;
            
            var db = GetDatabase();
            
            db.BeginTrans();
            
            try
            {
                var peopleCol = GetCollection<Person>("people");
                var addressesCol = GetCollection<Address>("addresses");
                var emailsCol = GetCollection<EmailAddress>("emails");

                // Insert all people at once
                peopleCol.Insert(peopleList);
                
                // Process addresses - extract all at once with LINQ
                var addresses = peopleList
                    .Where(p => p.Addresses?.Any() == true)
                    .SelectMany(p => p.Addresses.Select(a => 
                    {
                        a.PersonId = p.Id;
                        return a;
                    }))
                    .ToList();
                
                // Process emails - extract all at once with LINQ
                var emails = peopleList
                    .Where(p => p.EmailAddresses?.Any() == true)
                    .SelectMany(p => p.EmailAddresses.Select(e => 
                    {
                        e.PersonId = p.Id;
                        return e;
                    }))
                    .ToList();
                
                // Insert related data
                if (addresses.Any())
                    addressesCol.Insert(addresses);
                
                if (emails.Any())
                    emailsCol.Insert(emails);
                    
                db.Commit();
            }
            catch
            {
                db.Rollback();
                throw;
            }
        });
    }

    private void CreateIndices(LiteCollection<Person> peopleCol, 
                              LiteCollection<Address> addressesCol, 
                              LiteCollection<EmailAddress> emailsCol)
    {
        // Use thread-safe singleton pattern for index creation
        if (!_indicesCreated)
        {
            lock (_indicesLock)
            {
                if (!_indicesCreated)
                {
                    // Create indices for People collection
                    peopleCol.EnsureIndex(x => x.FirstName);
                    peopleCol.EnsureIndex(x => x.LastName);
                    // Composite index for efficient name search
                    peopleCol.EnsureIndex("$.FirstName + $.LastName");
                    
                    // Create indices for Address collection
                    addressesCol.EnsureIndex(x => x.PersonId);
                    addressesCol.EnsureIndex(x => x.Type);
                    addressesCol.EnsureIndex(x => x.IsPrimary);
                    // Compound index for common queries (e.g., primary home address)
                    addressesCol.EnsureIndex("$.PersonId + $.IsPrimary");
                    addressesCol.EnsureIndex("$.PersonId + $.Type");
                    
                    // Create indices for EmailAddress collection
                    emailsCol.EnsureIndex(x => x.PersonId);
                    emailsCol.EnsureIndex(x => x.Type);
                    emailsCol.EnsureIndex(x => x.IsPrimary);
                    // Compound index for common queries (e.g., primary email)
                    emailsCol.EnsureIndex("$.PersonId + $.IsPrimary");
                    
                    _indicesCreated = true;
                }
            }
        }
    }

    public Task<List<Person>> GetPeopleAsync()
    {
        return Task.Run(() =>
        {
            var db = GetDatabase();
            var peopleCol = GetCollection<Person>("people");
            var addressesCol = GetCollection<Address>("addresses");
            var emailsCol = GetCollection<EmailAddress>("emails");

            // Query optimization - use FindAll() for better performance
            var people = peopleCol.FindAll().ToList();
            if (!people.Any()) return people; // Early return if empty

            // Get all IDs to fetch only relevant related data
            var personIds = people.Select(p => p.Id).ToArray();
            
            // Get all related data in one query each with optimized filters
            // Use direct queries instead of Query.In with BsonArray
            var allAddresses = addressesCol.Find(a => personIds.Contains(a.PersonId))
                .ToLookup(a => a.PersonId);

            var allEmails = emailsCol.Find(e => personIds.Contains(e.PersonId))
                .ToLookup(e => e.PersonId);

            // Assign related data to people - more efficient than GroupBy+Dictionary
            foreach (var person in people)
            {
                person.Addresses = allAddresses[person.Id].ToList();
                person.EmailAddresses = allEmails[person.Id].ToList();
            }

            return people;
        });
    }

    public Task UpdatePeopleAsync(IEnumerable<Person> people)
    {
        return Task.Run(() =>
        {
            var peopleList = people.ToList(); // Materialize once
            if (!peopleList.Any()) return; // Early return
            
            var db = GetDatabase();
            
            db.BeginTrans();
            
            try
            {
                var peopleCol = GetCollection<Person>("people");
                
                // Process all updates in one pass
                foreach (var person in peopleList)
                {
                    peopleCol.Update(person);
                }
                
                db.Commit();
            }
            catch
            {
                db.Rollback();
                throw;
            }
        });
    }

    public Task DeleteAllPeopleAsync()
    {
        return Task.Run(() =>
        {
            var db = GetDatabase();
            
            db.BeginTrans();
            
            try
            {
                // Delete collections
                GetCollection<Person>("people").DeleteAll();
                GetCollection<Address>("addresses").DeleteAll();
                GetCollection<EmailAddress>("emails").DeleteAll();
                
                db.Commit();
            }
            catch
            {
                db.Rollback();
                throw;
            }
        });
    }

    public async Task<double> GetDatabaseSizeInMb()
    {
        return await Task.Run(() =>
        {
            try
            {
                var fileInfo = new FileInfo(_dbPath);
                if (fileInfo.Exists)
                {
                    return Math.Round(fileInfo.Length / (1024.0 * 1024.0), 2);
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        });
    }

    public void Dispose()
    {
        if (_database != null)
        {
            _databaseLock.Wait();
            try
            {
                _database?.Dispose();
                _database = null;
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        
        _databaseLock.Dispose();
    }
}