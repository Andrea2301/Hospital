using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace HospitalSV.Common;

/// <summary>
/// Enforces enterprise-grade password complexity:
/// - Minimum 8 characters
/// - At least one uppercase letter (A-Z)
/// - At least one lowercase letter (a-z)
/// - At least one decimal digit (0-9)
/// - At least one special symbol (!@#$%^&amp;*()_+-=[]{};':&quot;|,./?)
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class PasswordPolicyAttribute : ValidationAttribute
{
    private static readonly Regex HasUpperChar = new(@"[A-Z]", RegexOptions.Compiled);
    private static readonly Regex HasLowerChar = new(@"[a-z]", RegexOptions.Compiled);
    private static readonly Regex HasDigit     = new(@"[0-9]", RegexOptions.Compiled);
    private static readonly Regex HasSymbol    = new(@"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]", RegexOptions.Compiled);

    public int MinimumLength { get; set; } = 8;
    public int MaximumLength { get; set; } = 128;

    public PasswordPolicyAttribute()
    {
        ErrorMessage = "Password must be at least 8 characters long and contain at least one uppercase letter, one lowercase letter, one digit, and one special character.";
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
            return new ValidationResult("Password is required.");

        var password = value.ToString();
        if (string.IsNullOrEmpty(password))
            return new ValidationResult("Password cannot be empty.");

        if (password.Length < MinimumLength)
            return new ValidationResult($"Password must be at least {MinimumLength} characters long.");

        if (password.Length > MaximumLength)
            return new ValidationResult($"Password cannot exceed {MaximumLength} characters.");

        if (!HasUpperChar.IsMatch(password))
            return new ValidationResult("Password must contain at least one uppercase letter.");

        if (!HasLowerChar.IsMatch(password))
            return new ValidationResult("Password must contain at least one lowercase letter.");

        if (!HasDigit.IsMatch(password))
            return new ValidationResult("Password must contain at least one numeric digit.");

        if (!HasSymbol.IsMatch(password))
            return new ValidationResult("Password must contain at least one special character (!@#$%^&*...).");

        return ValidationResult.Success;
    }
}
