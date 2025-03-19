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
        
        // Configure LiteDB mapper for proper embedded document handling
        ConfigureLiteDBMapper();
        
        // Clean the database if requested
        if (_cleanOnStartup && File.Exists(_dbPath))
        {
            // Execute synchronously to ensure cleanup before first access
            CleanDatabaseAsync().GetAwaiter().GetResult();
        }
    }

    private void ConfigureLiteDBMapper()
    {
        // Configure the global mapper to properly handle our model classes
        var mapper = BsonMapper.Global;
        
        // Configure Person entity
        mapper.Entity<Person>()
              .Id(p => p.Id)
              // Explicitly include the collections that have [Ignore] attribute from SQLite
              .Field(p => p.Addresses, "Addresses")
              .Field(p => p.EmailAddresses, "EmailAddresses");
        
        // Configure Address entity to be stored as embedded document
        mapper.Entity<Address>()
              .Id(a => a.Id)
              // Don't store PersonId in embedded documents - it's redundant
              .Ignore(a => a.PersonId);
        
        // Configure EmailAddress entity to be stored as embedded document
        mapper.Entity<EmailAddress>()
              .Id(e => e.Id)
              // Don't store PersonId in embedded documents - it's redundant
              .Ignore(e => e.PersonId);
    }

    public LiteDatabase GetDatabase()
    {
        if (_database == null)
        {
            _databaseLock.Wait();
            try
            {
                _database ??= new LiteDatabase(new ConnectionString
                {
                    Filename = _dbPath,
                    // Performance optimizations
                    Connection = ConnectionType.Direct,
                    // Journal can be disabled for faster writes, but less protection against corruption
                    // Only use this if you can risk data loss
                    // Journal = false
                });
                
                // Create indices if not done already
                if (!_indicesCreated)
                {
                    CreateIndices(GetCollection<Person>("people"));
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
            int affectedRows = 0;
            
            db.BeginTrans();
            
            try
            {
                var peopleCol = GetCollection<Person>("people");
                
                // Remove any redundant PersonId values in embedded collections
                // This prevents duplication of data in the database
                foreach (var person in peopleList)
                {
                    foreach (var address in person.Addresses)
                    {
                        address.PersonId = person.Id;
                    }
                    
                    foreach (var email in person.EmailAddresses)
                    {
                        email.PersonId = person.Id;
                    }
                }
                
                // Insert all people with their embedded collections
                affectedRows = peopleCol.Insert(peopleList);
                
                db.Commit();
                
                return (affectedRows, GetDatabaseSize());
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
            var peopleCol = GetCollection<Person>("people");

            // Query all people with their embedded collections in a single operation
            var people = peopleCol.FindAll().ToList();
            
            // Ensure PersonId is properly set for all embedded objects
            // This maintains compatibility with other parts of the application
            foreach (var person in people)
            {
                foreach (var address in person.Addresses)
                {
                    address.PersonId = person.Id;
                }
                
                foreach (var email in person.EmailAddresses)
                {
                    email.PersonId = person.Id;
                }
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
                
                // Update Person objects with embedded collections
                foreach (var person in peopleList)
                {
                    // Update PersonId in embedded collections to maintain consistency
                    foreach (var address in person.Addresses)
                    {
                        address.PersonId = person.Id;
                    }
                    
                    foreach (var email in person.EmailAddresses)
                    {
                        email.PersonId = person.Id;
                    }
                    
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
            
            db.BeginTrans();
            
            try
            {
                var peopleCol = GetCollection<Person>("people");
                
                // Get count before deletion
                var affectedRows = peopleCol.Count();
                
                // Delete all people (addresses and emails are deleted automatically)
                peopleCol.DeleteAll();
                
                db.Commit();
                return (affectedRows, GetDatabaseSize());
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
        return await Task.Run(() => GetDatabaseSize());
    }

    private void CreateIndices(LiteCollection<Person> peopleCol)
    {
        lock (_indicesLock)
        {
            if (_indicesCreated) return;

            // Create indices for Person collection
            peopleCol.EnsureIndex(x => x.FirstName);
            peopleCol.EnsureIndex(x => x.LastName);
            
            // Compound index for name searches
            peopleCol.EnsureIndex("$.FirstName + $.LastName");
            
            // Indices for embedded collections for faster queries
            // Fixed potential typo from "$.Addressed[*].OsPrimary" to correct "$.Addresses[*].IsPrimary"
            // peopleCol.EnsureIndex("$.Addresses[*].IsPrimary", "addressPrimary");
            // peopleCol.EnsureIndex("$.Addresses[*].Type", "addressType");
            // peopleCol.EnsureIndex("$.EmailAddresses[*].IsPrimary", "emailPrimary");
            // peopleCol.EnsureIndex("$.EmailAddresses[*].Type", "emailType");
            // peopleCol.EnsureIndex("$.EmailAddresses[*].Email", "emailAddress");

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