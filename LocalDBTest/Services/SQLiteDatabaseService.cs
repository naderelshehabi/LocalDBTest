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
}

public class SQLiteDatabaseService : ISQLiteDatabaseService
{
    private SQLiteAsyncConnection _database;
    private bool _isInitialized;

    public SQLiteDatabaseService()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "people.db");
        _database = new SQLiteAsyncConnection(dbPath);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        await _database.CreateTableAsync<Person>();
        await _database.CreateTableAsync<Address>();
        await _database.CreateTableAsync<EmailAddress>();

        _isInitialized = true;
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
        await _database.RunInTransactionAsync((SQLiteConnection conn) =>
        {
            // Bulk insert people
            conn.InsertAll(people);

            // Prepare all addresses with person IDs
            var addresses = people.SelectMany(p => p.Addresses.Select(a =>
            {
                a.PersonId = p.Id;
                return a;
            })).ToList();

            // Prepare all emails with person IDs
            var emails = people.SelectMany(p => p.EmailAddresses.Select(e =>
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
        await _database.RunInTransactionAsync((SQLiteConnection conn) =>
        {
            conn.UpdateAll(people);
        });
    }

    public async Task DeletePeopleAsync(IEnumerable<Person> people)
    {
        await InitializeAsync();
        var personIds = people.Select(p => p.Id).ToArray();

        await _database.RunInTransactionAsync((SQLiteConnection conn) =>
        {
            // Delete all related records in a single query per table
            conn.Execute("DELETE FROM Address WHERE PersonId IN (" + string.Join(",", personIds) + ")");
            conn.Execute("DELETE FROM EmailAddress WHERE PersonId IN (" + string.Join(",", personIds) + ")");
            conn.Execute("DELETE FROM Person WHERE Id IN (" + string.Join(",", personIds) + ")");
        });
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
        var personIds = people.Select(p => p.Id).ToArray();

        // Fetch all related data in bulk
        var addresses = await _database.Table<Address>()
            .Where(a => personIds.Contains(a.PersonId))
            .ToListAsync();

        var emails = await _database.Table<EmailAddress>()
            .Where(e => personIds.Contains(e.PersonId))
            .ToListAsync();

        // Group and assign related data
        var addressMap = addresses.GroupBy(a => a.PersonId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var emailMap = emails.GroupBy(e => e.PersonId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Assign related data to people
        foreach (var person in people)
        {
            person.Addresses = addressMap.TryGetValue(person.Id, out var addr) ? addr : new List<Address>();
            person.EmailAddresses = emailMap.TryGetValue(person.Id, out var mail) ? mail : new List<EmailAddress>();
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
            // Delete related records in a single query per table
            conn.Execute("DELETE FROM Address WHERE PersonId = ?", person.Id);
            conn.Execute("DELETE FROM EmailAddress WHERE PersonId = ?", person.Id);
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