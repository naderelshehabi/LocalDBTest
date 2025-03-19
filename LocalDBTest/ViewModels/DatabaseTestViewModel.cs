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

    // Performance metrics for LiteDB
    private long _liteDbInsertTime;
    private long _liteDbSelectTime;
    private long _liteDbUpdateTime;
    private long _liteDbDeleteTime;

    // Performance metrics for SQLite
    private long _sqliteInsertTime;
    private long _sqliteSelectTime;
    private long _sqliteUpdateTime;
    private long _sqliteDeleteTime;

    // New properties for affected rows - LiteDB
    private int _liteDbInsertedRows;
    private int _liteDbSelectedRows;
    private int _liteDbUpdatedRows;
    private int _liteDbDeletedRows;
    private double _liteDbSize;

    // New properties for affected rows - SQLite
    private int _sqliteInsertedRows;
    private int _sqliteSelectedRows;
    private int _sqliteUpdatedRows;
    private int _sqliteDeletedRows;
    private double _sqliteDbSize;

    public DatabaseTestViewModel(IDatabaseService liteDbService, ISQLiteDatabaseService sqliteService)
    {
        _liteDbService = liteDbService;
        _sqliteService = sqliteService;

        TestLiteDbCommand = new Command(async () => await TestLiteDb());
        TestSqliteCommand = new Command(async () => await TestSqlite());
        ResetMetricsCommand = new Command(ResetMetrics);
    }

    // Properties for LiteDB metrics
    public long LiteDbInsertTime
    {
        get => _liteDbInsertTime;
        set
        {
            if (_liteDbInsertTime != value)
            {
                _liteDbInsertTime = value;
                OnPropertyChanged();
            }
        }
    }

    public long LiteDbSelectTime
    {
        get => _liteDbSelectTime;
        set
        {
            if (_liteDbSelectTime != value)
            {
                _liteDbSelectTime = value;
                OnPropertyChanged();
            }
        }
    }

    public long LiteDbUpdateTime
    {
        get => _liteDbUpdateTime;
        set
        {
            if (_liteDbUpdateTime != value)
            {
                _liteDbUpdateTime = value;
                OnPropertyChanged();
            }
        }
    }

    public long LiteDbDeleteTime
    {
        get => _liteDbDeleteTime;
        set
        {
            if (_liteDbDeleteTime != value)
            {
                _liteDbDeleteTime = value;
                OnPropertyChanged();
            }
        }
    }

    // Properties for SQLite metrics
    public long SqliteInsertTime
    {
        get => _sqliteInsertTime;
        set
        {
            if (_sqliteInsertTime != value)
            {
                _sqliteInsertTime = value;
                OnPropertyChanged();
            }
        }
    }

    public long SqliteSelectTime
    {
        get => _sqliteSelectTime;
        set
        {
            if (_sqliteSelectTime != value)
            {
                _sqliteSelectTime = value;
                OnPropertyChanged();
            }
        }
    }

    public long SqliteUpdateTime
    {
        get => _sqliteUpdateTime;
        set
        {
            if (_sqliteUpdateTime != value)
            {
                _sqliteUpdateTime = value;
                OnPropertyChanged();
            }
        }
    }

    public long SqliteDeleteTime
    {
        get => _sqliteDeleteTime;
        set
        {
            if (_sqliteDeleteTime != value)
            {
                _sqliteDeleteTime = value;
                OnPropertyChanged();
            }
        }
    }

    public int LiteDbInsertedRows
    {
        get => _liteDbInsertedRows;
        set
        {
            if (_liteDbInsertedRows != value)
            {
                _liteDbInsertedRows = value;
                OnPropertyChanged();
            }
        }
    }

    public int LiteDbSelectedRows
    {
        get => _liteDbSelectedRows;
        set
        {
            if (_liteDbSelectedRows != value)
            {
                _liteDbSelectedRows = value;
                OnPropertyChanged();
            }
        }
    }

    public int LiteDbUpdatedRows
    {
        get => _liteDbUpdatedRows;
        set
        {
            if (_liteDbUpdatedRows != value)
            {
                _liteDbUpdatedRows = value;
                OnPropertyChanged();
            }
        }
    }

    public int LiteDbDeletedRows
    {
        get => _liteDbDeletedRows;
        set
        {
            if (_liteDbDeletedRows != value)
            {
                _liteDbDeletedRows = value;
                OnPropertyChanged();
            }
        }
    }

    public double LiteDbSize
    {
        get => _liteDbSize;
        set
        {
            if (_liteDbSize != value)
            {
                _liteDbSize = value;
                OnPropertyChanged();
            }
        }
    }

    public int SqliteInsertedRows
    {
        get => _sqliteInsertedRows;
        set
        {
            if (_sqliteInsertedRows != value)
            {
                _sqliteInsertedRows = value;
                OnPropertyChanged();
            }
        }
    }

    public int SqliteSelectedRows
    {
        get => _sqliteSelectedRows;
        set
        {
            if (_sqliteSelectedRows != value)
            {
                _sqliteSelectedRows = value;
                OnPropertyChanged();
            }
        }
    }

    public int SqliteUpdatedRows
    {
        get => _sqliteUpdatedRows;
        set
        {
            if (_sqliteUpdatedRows != value)
            {
                _sqliteUpdatedRows = value;
                OnPropertyChanged();
            }
        }
    }

    public int SqliteDeletedRows
    {
        get => _sqliteDeletedRows;
        set
        {
            if (_sqliteDeletedRows != value)
            {
                _sqliteDeletedRows = value;
                OnPropertyChanged();
            }
        }
    }

    public double SqliteDbSize
    {
        get => _sqliteDbSize;
        set
        {
            if (_sqliteDbSize != value)
            {
                _sqliteDbSize = value;
                OnPropertyChanged();
            }
        }
    }

    // Existing properties
    public ICommand TestLiteDbCommand { get; }
    public ICommand TestSqliteCommand { get; }
    public ICommand ResetMetricsCommand { get; }

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
            var people = GenerateRandomPeople(NumberOfRecords);
            
            // Insert Test
            LiteDbStatus = "Inserting records...";
            var insertSw = System.Diagnostics.Stopwatch.StartNew();
            await _liteDbService.SavePeopleWithRelationsAsync(people);
            insertSw.Stop();
            LiteDbInsertTime = insertSw.ElapsedMilliseconds;
            LiteDbInsertedRows = people.Count;
            LiteDbSize = await _liteDbService.GetDatabaseSizeInMb();
            
            // Select Test
            LiteDbStatus = "Selecting records...";
            var selectSw = System.Diagnostics.Stopwatch.StartNew();
            var loadedPeople = await _liteDbService.GetPeopleAsync();
            selectSw.Stop();
            LiteDbSelectTime = selectSw.ElapsedMilliseconds;
            LiteDbSelectedRows = loadedPeople.Count;
            LiteDbSize = await _liteDbService.GetDatabaseSizeInMb();
            
            // Update Test
            LiteDbStatus = "Updating records...";
            var updateSw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var person in loadedPeople)
            {
                person.FirstName = "Updated_" + person.FirstName;
            }
            await _liteDbService.UpdatePeopleAsync(loadedPeople);
            updateSw.Stop();
            LiteDbUpdateTime = updateSw.ElapsedMilliseconds;
            LiteDbUpdatedRows = loadedPeople.Count;
            LiteDbSize = await _liteDbService.GetDatabaseSizeInMb();
            
            // Delete Test
            LiteDbStatus = "Deleting records...";
            var deleteSw = System.Diagnostics.Stopwatch.StartNew();
            await _liteDbService.DeleteAllPeopleAsync();
            deleteSw.Stop();
            LiteDbDeleteTime = deleteSw.ElapsedMilliseconds;
            LiteDbDeletedRows = loadedPeople.Count;
            LiteDbSize = await _liteDbService.GetDatabaseSizeInMb();
            
            LiteDbStatus = "Test completed";
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
            var people = GenerateRandomPeople(NumberOfRecords);
            
            // Initialize
            await _sqliteService.InitializeAsync();
            
            // Insert Test
            SqliteStatus = "Inserting records...";
            var insertSw = System.Diagnostics.Stopwatch.StartNew();
            await _sqliteService.SavePeopleWithRelationsAsync(people);
            insertSw.Stop();
            SqliteInsertTime = insertSw.ElapsedMilliseconds;
            SqliteInsertedRows = people.Count;
            SqliteDbSize = await _sqliteService.GetDatabaseSizeInMb();
            
            // Select Test
            SqliteStatus = "Selecting records...";
            var selectSw = System.Diagnostics.Stopwatch.StartNew();
            var loadedPeople = await _sqliteService.GetPeopleAsync();
            selectSw.Stop();
            SqliteSelectTime = selectSw.ElapsedMilliseconds;
            SqliteSelectedRows = loadedPeople.Count;
            SqliteDbSize = await _sqliteService.GetDatabaseSizeInMb();
            
            // Update Test
            SqliteStatus = "Updating records...";
            var updateSw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var person in loadedPeople)
            {
                person.FirstName = "Updated_" + person.FirstName;
            }
            await _sqliteService.UpdatePeopleAsync(loadedPeople);
            updateSw.Stop();
            SqliteUpdateTime = updateSw.ElapsedMilliseconds;
            SqliteUpdatedRows = loadedPeople.Count;
            SqliteDbSize = await _sqliteService.GetDatabaseSizeInMb();
            
            // Delete Test
            SqliteStatus = "Deleting records...";
            var deleteSw = System.Diagnostics.Stopwatch.StartNew();
            await _sqliteService.DeletePeopleAsync(loadedPeople);
            deleteSw.Stop();
            SqliteDeleteTime = deleteSw.ElapsedMilliseconds;
            SqliteDeletedRows = loadedPeople.Count;
            SqliteDbSize = await _sqliteService.GetDatabaseSizeInMb();
            
            SqliteStatus = "Test completed";
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

    private void ResetMetrics()
    {
        // Reset LiteDB metrics
        LiteDbInsertTime = 0;
        LiteDbSelectTime = 0;
        LiteDbUpdateTime = 0;
        LiteDbDeleteTime = 0;
        LiteDbInsertedRows = 0;
        LiteDbSelectedRows = 0;
        LiteDbUpdatedRows = 0;
        LiteDbDeletedRows = 0;
        LiteDbSize = 0;
        LiteDbStatus = string.Empty;

        // Reset SQLite metrics
        SqliteInsertTime = 0;
        SqliteSelectTime = 0;
        SqliteUpdateTime = 0;
        SqliteDeleteTime = 0;
        SqliteInsertedRows = 0;
        SqliteSelectedRows = 0;
        SqliteUpdatedRows = 0;
        SqliteDeletedRows = 0;
        SqliteDbSize = 0;
        SqliteStatus = string.Empty;
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