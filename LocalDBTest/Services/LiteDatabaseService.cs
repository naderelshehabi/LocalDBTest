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

    public LiteDatabaseService()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _dbPath = Path.Combine(path, "app.db");
    }

    public LiteDatabase GetDatabase()
    {
        if (_database == null)
        {
            _databaseLock.Wait();
            try
            {
                _database ??= new LiteDatabase(_dbPath);
            }
            finally
            {
                _databaseLock.Release();
            }
        }
        return _database;
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
            var peopleList = people.ToList(); // Materialize the collection once
            if (!peopleList.Any()) return; // Early return if empty
            
            var db = GetDatabase();
            
            // Use BeginTrans to start a transaction
            db.BeginTrans();
            
            try
            {
                var peopleCol = GetCollection<Person>("people");
                var addressesCol = GetCollection<Address>("addresses");
                var emailsCol = GetCollection<EmailAddress>("emails");

                // Create indices if needed - thread-safe
                CreateIndices(peopleCol, addressesCol, emailsCol);

                // Insert people in bulk
                peopleCol.Insert(peopleList);

                // Prepare all relationships in one pass
                var addresses = new List<Address>();
                var emails = new List<EmailAddress>();

                foreach (var person in peopleList)
                {
                    foreach (var address in person.Addresses)
                    {
                        address.PersonId = person.Id;
                        addresses.Add(address);
                    }
                    
                    foreach (var email in person.EmailAddresses)
                    {
                        email.PersonId = person.Id;
                        emails.Add(email);
                    }
                }

                // Bulk insert related data
                if (addresses.Any())
                    addressesCol.Insert(addresses);
                
                if (emails.Any())
                    emailsCol.Insert(emails);
                    
                // Commit transaction
                db.Commit();
            }
            catch
            {
                // Rollback on error
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
                    peopleCol.EnsureIndex(x => x.FirstName);
                    peopleCol.EnsureIndex(x => x.LastName);
                    addressesCol.EnsureIndex(x => x.PersonId);
                    emailsCol.EnsureIndex(x => x.PersonId);
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
                
                // Use batch operations for large datasets
                if (peopleList.Count > 1000)
                {
                    // Use batch operations instead of individual updates
                    foreach (var batch in peopleList.Chunk(500))
                    {
                        foreach (var person in batch)
                        {
                            peopleCol.Update(person);
                        }
                    }
                }
                else
                {
                    // Standard approach for smaller datasets
                    foreach (var person in peopleList)
                    {
                        peopleCol.Update(person);
                    }
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