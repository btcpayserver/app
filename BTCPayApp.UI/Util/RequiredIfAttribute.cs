using System.ComponentModel.DataAnnotations;

namespace BTCPayApp.UI.Util;

public enum RequiredIfTargetValue
{
    NotNull
}

public class RequiredIfAttribute(string otherProperty, object? targetValue) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var otherPropertyValue = validationContext.ObjectType
            .GetProperty(otherProperty)?
            .GetValue(validationContext.ObjectInstance);
        if (otherPropertyValue is null || targetValue is RequiredIfTargetValue.NotNull ||
            !otherPropertyValue.Equals(targetValue)) return ValidationResult.Success;
        return string.IsNullOrWhiteSpace(value?.ToString())
            ? new ValidationResult(ErrorMessage ?? "This field is required.")
            : ValidationResult.Success;
    }
}
