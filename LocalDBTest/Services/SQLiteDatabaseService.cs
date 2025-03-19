using SQLite;
using LocalDBTest.Models;
using System.Linq;

namespace LocalDBTest.Services;

public interface ISQLiteDatabaseService
{
    Task InitializeAsync();
    Task<(List<Person> people, double dbSize)> GetPeopleAsync();
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
    Task<(int affectedRows, double dbSize)> SavePeopleWithRelationsAsync(IEnumerable<Person> people);
    Task<(int affectedRows, double dbSize)> DeletePeopleAsync(IEnumerable<Person> people);
    Task<(int affectedRows, double dbSize)> UpdatePeopleAsync(IEnumerable<Person> people);
    Task CleanDatabaseAsync();
    Task<double> GetDatabaseSizeInMb();
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

    private async Task RunInTransactionAsync(Action<SQLiteConnection> action)
    {
        await InitializeAsync();
        await _database.RunInTransactionAsync(action);
    }

    public async Task RunInTransactionAsync(Func<SQLiteAsyncConnection, Task> action)
    {
        await InitializeAsync();
        // Create a wrapper that converts the async operation to sync
        await _database.RunInTransactionAsync(conn =>
        {
            // We need to run the async operation synchronously here
            // because SQLite transactions must be synchronous
            action(_database).Wait();
        });
    }

    public async Task<(int affectedRows, double dbSize)> SavePeopleWithRelationsAsync(IEnumerable<Person> people)
    {
        await InitializeAsync();
        var affectedRows = 0;
        var peopleList = people.ToList();

        await _database.RunInTransactionAsync((conn) =>
        {
            foreach (var person in peopleList)
            {
                // Insert person and count only person records
                conn.Insert(person);
                affectedRows++;

                // Insert addresses without counting
                if (person.Addresses?.Any() == true)
                {
                    foreach (var address in person.Addresses)
                    {
                        address.PersonId = person.Id;
                    }
                    conn.InsertAll(person.Addresses);
                }

                // Insert emails without counting
                if (person.EmailAddresses?.Any() == true)
                {
                    foreach (var email in person.EmailAddresses)
                    {
                        email.PersonId = person.Id;
                    }
                    conn.InsertAll(person.EmailAddresses);
                }
            }
        });

        return (affectedRows, await GetDatabaseSizeInMb());
    }

    public async Task<(int affectedRows, double dbSize)> UpdatePeopleAsync(IEnumerable<Person> people)
    {
        await InitializeAsync();
        var totalAffectedRows = 0;
        var peopleList = people.ToList();

        await _database.RunInTransactionAsync((conn) =>
        {
            foreach (var person in peopleList)
            {
                if (conn.Update(person) > 0)
                    totalAffectedRows++;
            }
        });

        return (totalAffectedRows, await GetDatabaseSizeInMb());
    }

    public async Task<(int affectedRows, double dbSize)> DeletePeopleAsync(IEnumerable<Person> people)
    {
        await InitializeAsync();
        var peopleList = people.ToList();
        
        if (!peopleList.Any())
            return (0, await GetDatabaseSizeInMb());
            
        var affectedRows = 0;
        var personIds = peopleList.Select(p => p.Id).ToList();

        // Use a single transaction for better performance
        await _database.RunInTransactionAsync((conn) =>
        {
            // Delete all related records in bulk with IN clause
            var idsParam = string.Join(",", personIds);
            
            // Delete addresses in bulk
            conn.Execute($"DELETE FROM Addresses WHERE PersonId IN ({idsParam})");
            
            // Delete emails in bulk
            conn.Execute($"DELETE FROM EmailAddresses WHERE PersonId IN ({idsParam})");
            
            // Delete people in bulk and count affected rows
            affectedRows = conn.Execute($"DELETE FROM People WHERE Id IN ({idsParam})");
        });

        return (affectedRows, await GetDatabaseSizeInMb());
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

    public async Task<(List<Person> people, double dbSize)> GetPeopleAsync()
    {
        await InitializeAsync();
        var people = await _database.Table<Person>().ToListAsync();

        if (people.Any())
        {
            // Get all person IDs for batch operations
            var personIds = people.Select(p => p.Id).ToList();

            // Fetch all related data in bulk with a single query each
            var allAddresses = await _database.Table<Address>()
                .Where(a => personIds.Contains(a.PersonId))
                .ToListAsync();

            var allEmails = await _database.Table<EmailAddress>()
                .Where(e => personIds.Contains(e.PersonId))
                .ToListAsync();

            // Organize related data by person ID
            var addressesByPersonId = allAddresses.GroupBy(a => a.PersonId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var emailsByPersonId = allEmails.GroupBy(e => e.PersonId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Assign related data to each person
            foreach (var person in people)
            {
                person.Addresses = addressesByPersonId.ContainsKey(person.Id) 
                    ? addressesByPersonId[person.Id] 
                    : new List<Address>();
                    
                person.EmailAddresses = emailsByPersonId.ContainsKey(person.Id)
                    ? emailsByPersonId[person.Id]
                    : new List<EmailAddress>();
            }
        }

        return (people, await GetDatabaseSizeInMb());
    }

    public async Task<Person?> GetPersonAsync(int id)
    {
        await InitializeAsync();
        
        // Use parameterized SQL query for better optimization
        var person = await _database.QueryAsync<Person>("SELECT * FROM People WHERE Id = ? LIMIT 1", id)
            .ContinueWith(t => t.Result.FirstOrDefault());
            
        if (person != null)
        {
            // Get related data using optimized methods, already running in parallel
            var addressesTask = GetAddressesForPersonAsync(id);
            var emailsTask = GetEmailsForPersonAsync(id);
            
            // Wait for both tasks to complete
            await Task.WhenAll(addressesTask, emailsTask);
            
            // Assign the results
            person.Addresses = await addressesTask;
            person.EmailAddresses = await emailsTask;
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
        // Using parameter binding which allows SQLite to optimize the query execution plan
        return await _database.QueryAsync<Address>(
            "SELECT * FROM Addresses WHERE PersonId = ? ORDER BY IsPrimary DESC", 
            personId);
    }

    public async Task<List<EmailAddress>> GetEmailsForPersonAsync(int personId)
    {
        await InitializeAsync();
        // Using parameter binding and ordering by IsPrimary for better performance and usability
        return await _database.QueryAsync<EmailAddress>(
            "SELECT * FROM EmailAddresses WHERE PersonId = ? ORDER BY IsPrimary DESC", 
            personId);
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

    public async Task<double> GetDatabaseSizeInMb()
    {
        await InitializeAsync();
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
}