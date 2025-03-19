using SQLite;
using System.Collections.Generic;

namespace LocalDBTest.Models;

[Table("People")]
public class Person
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    [Indexed, NotNull]
    public string FirstName { get; set; } = string.Empty;
    [Indexed, NotNull]
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    [Ignore]
    public List<Address> Addresses { get; set; } = new();
    [Ignore]
    public List<EmailAddress> EmailAddresses { get; set; } = new();
}

[Table("Addresses")]
public class Address
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    [Indexed]
    public int PersonId { get; set; }
    [NotNull]
    public string Street { get; set; } = string.Empty;
    [NotNull]
    public string City { get; set; } = string.Empty;
    [NotNull]
    public string State { get; set; } = string.Empty;
    [NotNull]
    public string PostalCode { get; set; } = string.Empty;
    [NotNull]
    public string Country { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public AddressType Type { get; set; }
}

[Table("EmailAddresses")]
public class EmailAddress
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    [Indexed]
    public int PersonId { get; set; }
    [NotNull]
    public string Email { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public EmailType Type { get; set; }
}

public enum AddressType
{
    Home,
    Work,
    Other
}

public enum EmailType
{
    Personal,
    Work,
    Other
}