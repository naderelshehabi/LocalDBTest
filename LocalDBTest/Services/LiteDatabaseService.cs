using LiteDB;
using System.IO;
using LocalDBTest.Models;
using System.Collections.Concurrent;

namespace LocalDBTest.Services;

public interface IDatabaseService
{
    LiteDatabase GetDatabase();
    Task<(int affectedRows, double dbSize)> SavePeopleWithRelationsAsync(IEnumerable<Person> people);
    Task<(List<Person> people, double dbSize)> GetPeopleAsync();
    Task<(int affectedRows, double dbSize)> UpdatePeopleAsync(IEnumerable<Person> people);
    Task<(int affectedRows, double dbSize)> DeleteAllPeopleAsync();
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

    public async Task<(int affectedRows, double dbSize)> SavePeopleWithRelationsAsync(IEnumerable<Person> people)
    {
        return await Task.Run(() =>
        {
            var peopleList = people.ToList();
            if (!peopleList.Any()) return (0, 0);
            
            var db = GetDatabase();
            var totalAffectedRows = 0;
            
            db.BeginTrans();
            
            try
            {
                var peopleCol = GetCollection<Person>("people");
                var addressesCol = GetCollection<Address>("addresses");
                var emailsCol = GetCollection<EmailAddress>("emails");

                // Insert all people at once
                totalAffectedRows += peopleCol.Insert(peopleList);
                
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
                    totalAffectedRows += addressesCol.Insert(addresses);
                
                if (emails.Any())
                    totalAffectedRows += emailsCol.Insert(emails);
                    
                db.Commit();
                
                return (totalAffectedRows, GetDatabaseSize());
            }
            catch
            {
                db.Rollback();
                throw;
            }
        });
    }

    public async Task<(List<Person> people, double dbSize)> GetPeopleAsync()
    {
        return await Task.Run(() =>
        {
            var db = GetDatabase();
            var peopleCol = GetCollection<Person>("people");
            var addressesCol = GetCollection<Address>("addresses");
            var emailsCol = GetCollection<EmailAddress>("emails");

            // Query optimization - use FindAll() for better performance
            var people = peopleCol.FindAll().ToList();
            if (!people.Any()) return (people, GetDatabaseSize()); // Early return if empty

            var personIds = people.Select(p => p.Id).ToArray();
            
            // Get all related data in one query each with optimized filters
            var allAddresses = addressesCol.Find(a => personIds.Contains(a.PersonId))
                .ToLookup(a => a.PersonId);

            var allEmails = emailsCol.Find(e => personIds.Contains(e.PersonId))
                .ToLookup(e => e.PersonId);

            // Assign related data to people
            foreach (var person in people)
            {
                person.Addresses = allAddresses[person.Id].ToList();
                person.EmailAddresses = allEmails[person.Id].ToList();
            }

            return (people, GetDatabaseSize());
        });
    }

    public async Task<(int affectedRows, double dbSize)> UpdatePeopleAsync(IEnumerable<Person> people)
    {
        return await Task.Run(() =>
        {
            var peopleList = people.ToList();
            if (!peopleList.Any()) return (0, 0);
            
            var db = GetDatabase();
            var totalAffectedRows = 0;
            
            db.BeginTrans();
            
            try
            {
                var peopleCol = GetCollection<Person>("people");
                
                foreach (var person in peopleList)
                {
                    if (peopleCol.Update(person))
                        totalAffectedRows++;
                }
                
                db.Commit();
                return (totalAffectedRows, GetDatabaseSize());
            }
            catch
            {
                db.Rollback();
                throw;
            }
        });
    }

    public async Task<(int affectedRows, double dbSize)> DeleteAllPeopleAsync()
    {
        return await Task.Run(() =>
        {
            var db = GetDatabase();
            var totalAffectedRows = 0;
            
            db.BeginTrans();
            
            try
            {
                var peopleCol = GetCollection<Person>("people");
                var addressesCol = GetCollection<Address>("addresses");
                var emailsCol = GetCollection<EmailAddress>("emails");

                // Get count of records that will be deleted
                totalAffectedRows += peopleCol.Count();
                totalAffectedRows += addressesCol.Count();
                totalAffectedRows += emailsCol.Count();

                // Delete collections
                peopleCol.DeleteAll();
                addressesCol.DeleteAll();
                emailsCol.DeleteAll();
                
                db.Commit();
                return (totalAffectedRows, GetDatabaseSize());
            }
            catch
            {
                db.Rollback();
                throw;
            }
        });
    }

    private double GetDatabaseSize()
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

    private void CreateIndices(LiteCollection<Person> peopleCol, LiteCollection<Address> addressesCol, LiteCollection<EmailAddress> emailsCol)
    {
        lock (_indicesLock)
        {
            if (_indicesCreated) return;

            // Create indices for Person collection
            peopleCol.EnsureIndex(x => x.FirstName);
            peopleCol.EnsureIndex(x => x.LastName);

            // Create indices for Address collection
            addressesCol.EnsureIndex(x => x.PersonId);
            addressesCol.EnsureIndex(x => x.Type);
            addressesCol.EnsureIndex(x => x.IsPrimary);

            // Create indices for EmailAddress collection
            emailsCol.EnsureIndex(x => x.PersonId);
            emailsCol.EnsureIndex(x => x.Type);
            emailsCol.EnsureIndex(x => x.IsPrimary);

            _indicesCreated = true;
        }
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