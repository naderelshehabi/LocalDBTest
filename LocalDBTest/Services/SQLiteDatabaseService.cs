using SQLite;
using LocalDBTest.Models;

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

    public async Task<List<Person>> GetPeopleAsync()
    {
        await InitializeAsync();
        var people = await _database.Table<Person>().ToListAsync();
        foreach (var person in people)
        {
            person.Addresses = await GetAddressesForPersonAsync(person.Id);
            person.EmailAddresses = await GetEmailsForPersonAsync(person.Id);
        }
        return people;
    }

    public async Task<Person?> GetPersonAsync(int id)
    {
        await InitializeAsync();
        var person = await _database.GetAsync<Person>(id);
        if (person != null)
        {
            person.Addresses = await GetAddressesForPersonAsync(id);
            person.EmailAddresses = await GetEmailsForPersonAsync(id);
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
        // Delete related addresses and emails first
        await _database.Table<Address>().DeleteAsync(a => a.PersonId == person.Id);
        await _database.Table<EmailAddress>().DeleteAsync(e => e.PersonId == person.Id);
        return await _database.DeleteAsync(person);
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