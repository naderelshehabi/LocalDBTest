using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalDBTest.Models;
using LocalDBTest.Services;
using System.Windows.Input;

namespace LocalDBTest.ViewModels;

public class DatabaseTestViewModel : INotifyPropertyChanged
{
    private readonly IDatabaseService _liteDbService;
    private readonly ISQLiteDatabaseService _sqliteService;
    private int _numberOfRecords = 1;
    private string _liteDbStatus = string.Empty;
    private string _sqliteStatus = string.Empty;
    private bool _isBusy;

    public DatabaseTestViewModel(IDatabaseService liteDbService, ISQLiteDatabaseService sqliteService)
    {
        _liteDbService = liteDbService;
        _sqliteService = sqliteService;

        TestLiteDbCommand = new Command(async () => await TestLiteDb());
        TestSqliteCommand = new Command(async () => await TestSqlite());
    }

    public ICommand TestLiteDbCommand { get; }
    public ICommand TestSqliteCommand { get; }

    public int NumberOfRecords
    {
        get => _numberOfRecords;
        set
        {
            if (_numberOfRecords != value)
            {
                _numberOfRecords = value;
                OnPropertyChanged();
            }
        }
    }

    public string LiteDbStatus
    {
        get => _liteDbStatus;
        set
        {
            if (_liteDbStatus != value)
            {
                _liteDbStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public string SqliteStatus
    {
        get => _sqliteStatus;
        set
        {
            if (_sqliteStatus != value)
            {
                _sqliteStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }
    }

    private async Task TestLiteDb()
    {
        if (IsBusy) return;
        IsBusy = true;
        LiteDbStatus = "Starting LiteDB test...";

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var people = await Task.Run(() => GenerateRandomPeople(NumberOfRecords));
            
            // Insert Test
            LiteDbStatus = "Inserting records...";
            await Task.Run(() => {
                var db = _liteDbService.GetDatabase();
                var collection = db.GetCollection<Person>("people");
                collection.Insert(people);
            });
            
            // Select Test
            LiteDbStatus = "Selecting records...";
            var loadedPeople = await Task.Run(() => {
                var db = _liteDbService.GetDatabase();
                var collection = db.GetCollection<Person>("people");
                return collection.FindAll().ToList();
            });
            
            // Update Test
            LiteDbStatus = "Updating records...";
            await Task.Run(() => {
                var db = _liteDbService.GetDatabase();
                var collection = db.GetCollection<Person>("people");
                foreach (var person in loadedPeople)
                {
                    person.FirstName = "Updated_" + person.FirstName;
                    collection.Update(person);
                }
            });
            
            // Delete Test
            LiteDbStatus = "Deleting records...";
            await Task.Run(() => {
                var db = _liteDbService.GetDatabase();
                var collection = db.GetCollection<Person>("people");
                collection.DeleteAll();
            });
            
            sw.Stop();
            LiteDbStatus = $"Test completed in {sw.ElapsedMilliseconds}ms";
        }
        catch (Exception ex)
        {
            LiteDbStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TestSqlite()
    {
        if (IsBusy) return;
        IsBusy = true;
        SqliteStatus = "Starting SQLite test...";

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var people = GenerateRandomPeople(NumberOfRecords);
            
            // Initialize
            await _sqliteService.InitializeAsync();
            
            // Insert Test
            SqliteStatus = "Inserting records...";
            foreach (var person in people)
            {
                await _sqliteService.SavePersonAsync(person);
                foreach (var address in person.Addresses)
                {
                    address.PersonId = person.Id;
                    await _sqliteService.SaveAddressAsync(address);
                }
                foreach (var email in person.EmailAddresses)
                {
                    email.PersonId = person.Id;
                    await _sqliteService.SaveEmailAsync(email);
                }
            }
            
            // Select Test
            SqliteStatus = "Selecting records...";
            var loadedPeople = await _sqliteService.GetPeopleAsync();
            
            // Update Test
            SqliteStatus = "Updating records...";
            foreach (var person in loadedPeople)
            {
                person.FirstName = "Updated_" + person.FirstName;
                await _sqliteService.SavePersonAsync(person);
            }
            
            // Delete Test
            SqliteStatus = "Deleting records...";
            foreach (var person in loadedPeople)
            {
                await _sqliteService.DeletePersonAsync(person);
            }
            
            sw.Stop();
            SqliteStatus = $"Test completed in {sw.ElapsedMilliseconds}ms";
        }
        catch (Exception ex)
        {
            SqliteStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private List<Person> GenerateRandomPeople(int count)
    {
        var random = new Random();
        var firstNames = new[] { "John", "Jane", "Bob", "Alice", "Charlie", "Diana", "Edward", "Fiona" };
        var lastNames = new[] { "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis" };
        var cities = new[] { "New York", "Los Angeles", "Chicago", "Houston", "Phoenix", "Philadelphia" };
        var states = new[] { "NY", "CA", "IL", "TX", "AZ", "PA" };
        var emailDomains = new[] { "gmail.com", "yahoo.com", "hotmail.com", "outlook.com" };

        var people = new List<Person>();
        for (int i = 0; i < count; i++)
        {
            var person = new Person
            {
                FirstName = firstNames[random.Next(firstNames.Length)],
                LastName = lastNames[random.Next(lastNames.Length)],
                PhoneNumber = $"{random.Next(100, 999)}-{random.Next(100, 999)}-{random.Next(1000, 9999)}"
            };

            // Add 1-3 addresses
            for (int j = 0; j < random.Next(1, 4); j++)
            {
                person.Addresses.Add(new Address
                {
                    Street = $"{random.Next(100, 9999)} {lastNames[random.Next(lastNames.Length)]} St",
                    City = cities[random.Next(cities.Length)],
                    State = states[random.Next(states.Length)],
                    PostalCode = random.Next(10000, 99999).ToString(),
                    Country = "USA",
                    Type = (AddressType)random.Next(3),
                    IsPrimary = j == 0
                });
            }

            // Add 1-2 email addresses
            for (int j = 0; j < random.Next(1, 3); j++)
            {
                person.EmailAddresses.Add(new EmailAddress
                {
                    Email = $"{person.FirstName.ToLower()}.{person.LastName.ToLower()}{random.Next(100)}@{emailDomains[random.Next(emailDomains.Length)]}",
                    Type = (EmailType)random.Next(3),
                    IsPrimary = j == 0
                });
            }

            people.Add(person);
        }

        return people;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}