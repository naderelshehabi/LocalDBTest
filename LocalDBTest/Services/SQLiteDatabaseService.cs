using SQLite;
using LocalDBTest.Models;
using System.Linq;

namespace LocalDBTest.Services;

public interface ISQLiteDatabaseService
{
    Task InitializeAsync();
    Task<List<Person>> GetPeopleAsync();
    Task<Person?> GetPersonAsync(int id);
    Task<int> SavePersonAsync(Person person);
    Task<int> SaveAddressAsync(Address address);
    Task<int> SaveEmailAsync(EmailAddress email);
    Task<List<Address>> GetAddressesForPersonAsync(int personId);
    Task<List<EmailAddress>> GetEmailsForPersonAsync(int personId);
    Task<int> DeletePersonAsync(Person person);
    Task<int> DeleteAddressAsync(Address address);
    Task<int> DeleteEmailAsync(EmailAddress email);
    Task RunInTransactionAsync(Func<SQLiteAsyncConnection, Task> action);
    Task SavePeopleWithRelationsAsync(IEnumerable<Person> people);
    Task DeletePeopleAsync(IEnumerable<Person> people);
    Task UpdatePeopleAsync(IEnumerable<Person> people);
    Task CleanDatabaseAsync();
}

public class SQLiteDatabaseService : ISQLiteDatabaseService
{
    private SQLiteAsyncConnection _database;
    private bool _isInitialized;
    private readonly string _dbPath;
    
    // Add flag to determine if database should be cleaned on startup
    private readonly bool _cleanOnStartup;

    public SQLiteDatabaseService(bool cleanOnStartup = false)
    {
        _cleanOnStartup = cleanOnStartup;
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "people.db");
        _database = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        // Clean database if requested
        if (_cleanOnStartup && File.Exists(_dbPath))
        {
            await CleanDatabaseAsync();
        }

        // Create tables and optimize with proper indices
        await _database.CreateTableAsync<Person>(CreateFlags.None);
        await _database.CreateTableAsync<Address>(CreateFlags.None);
        await _database.CreateTableAsync<EmailAddress>(CreateFlags.None);

        // Create optimal indices for better query performance
        await CreateIndicesAsync();

        _isInitialized = true;
    }

    public async Task CleanDatabaseAsync()
    {
        // Close the connection first
        await _database.CloseAsync();

        // Delete the database file
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        // Recreate the connection
        _database = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        _isInitialized = false;
    }

    private async Task CreateIndicesAsync()
    {
        // Create indices for Person table
        await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_person_firstname ON People(FirstName)");
        await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_person_lastname ON People(LastName)");
        
        // Create indices for Address table - essential for relationship lookups
        await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_address_personid ON Addresses(PersonId)");
        await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_address_type ON Addresses(Type)");
        await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_address_isprimary ON Addresses(IsPrimary)");
        
        // Create indices for EmailAddress table
        await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_email_personid ON EmailAddresses(PersonId)");
        await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_email_type ON EmailAddresses(Type)");
        await _database.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_email_isprimary ON EmailAddresses(IsPrimary)");
    }

    public async Task RunInTransactionAsync(Func<SQLiteAsyncConnection, Task> action)
    {
        await InitializeAsync();
        await _database.RunInTransactionAsync((SQLiteConnection conn) =>
        {
            // We can't use async/await directly in the transaction action
            // so we'll run the async operation synchronously
            action(_database).Wait();
        });
    }

    public async Task SavePeopleWithRelationsAsync(IEnumerable<Person> people)
    {
        await InitializeAsync();
        
        var peopleList = people.ToList();
        if (!peopleList.Any()) return;
        
        await _database.RunInTransactionAsync((SQLiteConnection conn) =>
        {
            // Bulk insert people
            conn.InsertAll(peopleList);

            // Prepare all addresses with person IDs
            var addresses = peopleList.SelectMany(p => p.Addresses.Select(a =>
            {
                a.PersonId = p.Id;
                return a;
            })).ToList();

            // Prepare all emails with person IDs
            var emails = peopleList.SelectMany(p => p.EmailAddresses.Select(e =>
            {
                e.PersonId = p.Id;
                return e;
            })).ToList();

            // Bulk insert related data
            if (addresses.Any())
                conn.InsertAll(addresses);
            
            if (emails.Any())
                conn.InsertAll(emails);
        });
    }

    public async Task UpdatePeopleAsync(IEnumerable<Person> people)
    {
        await InitializeAsync();
        
        var peopleList = people.ToList();
        if (!peopleList.Any()) return;
        
        await _database.RunInTransactionAsync((SQLiteConnection conn) =>
        {
            conn.UpdateAll(peopleList);
        });
    }

    public async Task DeletePeopleAsync(IEnumerable<Person> people)
    {
        await InitializeAsync();
        
        var peopleList = people.ToList();
        if (!peopleList.Any()) return;
        
        var personIds = peopleList.Select(p => p.Id).ToList();
        
        // Use batching to avoid "Too many SQL variables" error
        const int batchSize = 500; // SQLite typically has a limit of around 999 variables
        
        // Process in batches
        for (int i = 0; i < personIds.Count; i += batchSize)
        {
            var batchIds = personIds.Skip(i).Take(batchSize).ToList();
            var batchPeople = peopleList.Where(p => batchIds.Contains(p.Id)).ToList();
            
            await _database.RunInTransactionAsync((SQLiteConnection conn) =>
            {
                // Delete related addresses for this batch using bulk delete
                conn.Table<Address>().Delete(a => batchIds.Contains(a.PersonId));
                
                // Delete related emails for this batch using bulk delete
                conn.Table<EmailAddress>().Delete(e => batchIds.Contains(e.PersonId));
                
                // Delete people in this batch using bulk delete if possible
                // Since we need to delete specific entities, we'll use a more efficient approach
                var deleteQuery = $"DELETE FROM People WHERE Id IN ({string.Join(",", batchIds)})";
                conn.Execute(deleteQuery);
            });
        }
    }

    public async Task<int> SaveAddressesAsync(IEnumerable<Address> addresses)
    {
        await InitializeAsync();
        return await _database.InsertAllAsync(addresses);
    }

    public async Task<int> SaveEmailsAsync(IEnumerable<EmailAddress> emails)
    {
        await InitializeAsync();
        return await _database.InsertAllAsync(emails);
    }

    public async Task<List<Person>> GetPeopleAsync()
    {
        await InitializeAsync();
        var people = await _database.Table<Person>().ToListAsync();
        if (!people.Any()) return people; // Early return if empty
        
        var personIds = people.Select(p => p.Id).ToList();

        // Use batching to avoid "Too many SQL variables" error
        const int batchSize = 500; // SQLite typically has a limit of around 999 variables
        var addresses = new List<Address>();
        var emails = new List<EmailAddress>();

        // Process in batches
        for (int i = 0; i < personIds.Count; i += batchSize)
        {
            var batchIds = personIds.Skip(i).Take(batchSize).ToList();
            
            // Fetch addresses for this batch
            var batchAddresses = await _database.Table<Address>()
                .Where(a => batchIds.Contains(a.PersonId))
                .ToListAsync();
            addresses.AddRange(batchAddresses);
            
            // Fetch emails for this batch
            var batchEmails = await _database.Table<EmailAddress>()
                .Where(e => batchIds.Contains(e.PersonId))
                .ToListAsync();
            emails.AddRange(batchEmails);
        }

        // Group and assign related data - use ToLookup for better performance
        var addressLookup = addresses.ToLookup(a => a.PersonId);
        var emailLookup = emails.ToLookup(e => e.PersonId);

        // Assign related data to people
        foreach (var person in people)
        {
            person.Addresses = addressLookup[person.Id].ToList();
            person.EmailAddresses = emailLookup[person.Id].ToList();
        }

        return people;
    }

    public async Task<Person?> GetPersonAsync(int id)
    {
        await InitializeAsync();
        var person = await _database.GetAsync<Person>(id);
        if (person != null)
        {
            // Get related data in parallel
            var addressTask = _database.Table<Address>()
                .Where(a => a.PersonId == id)
                .ToListAsync();

            var emailTask = _database.Table<EmailAddress>()
                .Where(e => e.PersonId == id)
                .ToListAsync();

            await Task.WhenAll(addressTask, emailTask);

            person.Addresses = await addressTask;
            person.EmailAddresses = await emailTask;
        }
        return person;
    }

    public async Task<int> SavePersonAsync(Person person)
    {
        await InitializeAsync();
        if (person.Id != 0)
        {
            return await _database.UpdateAsync(person);
        }
        return await _database.InsertAsync(person);
    }

    public async Task<int> SaveAddressAsync(Address address)
    {
        await InitializeAsync();
        if (address.Id != 0)
        {
            return await _database.UpdateAsync(address);
        }
        return await _database.InsertAsync(address);
    }

    public async Task<int> SaveEmailAsync(EmailAddress email)
    {
        await InitializeAsync();
        if (email.Id != 0)
        {
            return await _database.UpdateAsync(email);
        }
        return await _database.InsertAsync(email);
    }

    public async Task<List<Address>> GetAddressesForPersonAsync(int personId)
    {
        await InitializeAsync();
        return await _database.Table<Address>()
            .Where(a => a.PersonId == personId)
            .ToListAsync();
    }

    public async Task<List<EmailAddress>> GetEmailsForPersonAsync(int personId)
    {
        await InitializeAsync();
        return await _database.Table<EmailAddress>()
            .Where(e => e.PersonId == personId)
            .ToListAsync();
    }

    public async Task<int> DeletePersonAsync(Person person)
    {
        await InitializeAsync();
        await _database.RunInTransactionAsync((conn) =>
        {
            // Delete all related addresses with a single query using DeleteAll with a predicate
            conn.Table<Address>().Delete(a => a.PersonId == person.Id);
            
            // Delete all related emails with a single query using DeleteAll with a predicate
            conn.Table<EmailAddress>().Delete(e => e.PersonId == person.Id);
            
            // Delete the person
            conn.Delete(person);
        });
        return 1;
    }

    public async Task<int> DeleteAddressAsync(Address address)
    {
        await InitializeAsync();
        return await _database.DeleteAsync(address);
    }

    public async Task<int> DeleteEmailAsync(EmailAddress email)
    {
        await InitializeAsync();
        return await _database.DeleteAsync(email);
    }
}