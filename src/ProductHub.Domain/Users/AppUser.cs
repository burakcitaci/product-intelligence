namespace ProductHub.Domain.Users;

public enum AppUserRole
{
    User,
    Admin,
}

public sealed class AppUser
{
    private AppUser() { }

    private AppUser(
        Guid id,
        string email,
        string displayName,
        string passwordHash,
        AppUserRole role
    )
    {
        Id = id;
        Email = email.Trim().ToLowerInvariant();
        DisplayName = displayName.Trim();
        PasswordHash = passwordHash;
        Role = role;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public AppUserRole Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static AppUser Create(
        string email,
        string displayName,
        string passwordHash,
        AppUserRole role
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        if (!email.Contains('@'))
            throw new ArgumentException("A valid email address is required.", nameof(email));
        return new AppUser(Guid.NewGuid(), email, displayName, passwordHash, role);
    }
}
