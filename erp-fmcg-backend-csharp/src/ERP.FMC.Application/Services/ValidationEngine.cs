using System.Text.Json;
using System.Text.RegularExpressions;
using ERP.FMC.Application.DTOs;

namespace ERP.FMC.Application.Services;

/// <summary>
/// Validation engine cho import.
/// </summary>
public class ValidationEngine
{
    private readonly Dictionary<string, List<IValidationRule>> _rules = new();
    private readonly Dictionary<string, Func<string, object?>> _converters = new();

    public ValidationEngine()
    {
        RegisterDefaultRules();
        RegisterDefaultConverters();
    }

    public void RegisterRule(string entityType, IValidationRule rule)
    {
        if (!_rules.ContainsKey(entityType))
            _rules[entityType] = new List<IValidationRule>();
        _rules[entityType].Add(rule);
    }

    /// <summary>
    /// Validate một row (Dictionary field→value).
    /// </summary>
    public ValidationResult Validate(string entityType, Dictionary<string, string?> row, int rowNumber)
    {
        var result = new ValidationResult { IsValid = true };
        var parsed = new Dictionary<string, object?>();

        if (!_rules.ContainsKey(entityType))
        {
            // No rules = accept all
            foreach (var kvp in row)
                if (!string.IsNullOrEmpty(kvp.Key))
                    parsed[kvp.Key] = kvp.Value;
            result.ParsedValues = parsed;
            return result;
        }

        foreach (var rule in _rules[entityType])
        {
            var fieldName = rule.FieldName;
            var rawValue = row.GetValueOrDefault(fieldName)?.Trim() ?? (row.GetValueOrDefault(fieldName.ToLower())?.Trim());
            var value = rawValue;

            // Skip if not required and empty
            if (!rule.IsRequired && string.IsNullOrWhiteSpace(value))
                continue;

            // Required check
            if (rule.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    FieldName = fieldName,
                    FieldValue = value,
                    ErrorCode = "REQUIRED",
                    ErrorMessage = $"{rule.DisplayName ?? fieldName} không được để trống"
                });
                continue;
            }

            // Convert
            object? converted = null;
            var conversionOk = TryConvert(value, rule.DataType, fieldName, out converted);

            if (!conversionOk)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    FieldName = fieldName,
                    FieldValue = value,
                    ErrorCode = "INVALID_FORMAT",
                    ErrorMessage = $"{rule.DisplayName ?? fieldName} có định dạng không hợp lệ"
                });
                continue;
            }

            // Custom validation
            var customResult = rule.Validate(converted, value);
            if (!customResult.IsValid)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    FieldName = fieldName,
                    FieldValue = value,
                    ErrorCode = customResult.ErrorCode,
                    ErrorMessage = customResult.ErrorMessage ?? "Giá trị không hợp lệ",
                    SuggestedFix = customResult.SuggestedFix
                });
            }
            else if (customResult.HasWarning)
            {
                result.Warnings.Add(new ValidationWarning
                {
                    FieldName = fieldName,
                    FieldValue = value,
                    WarningCode = customResult.WarningCode ?? "WARNING",
                    WarningMessage = customResult.WarningMessage ?? "",
                    SuggestedFix = customResult.SuggestedFix
                });
            }

            parsed[fieldName] = converted;
        }

        result.ParsedValues = parsed;
        return result;
    }

    private void RegisterDefaultRules()
    {
        // ========== PARTNERS ==========
        RegisterRule("PARTNERS", new RequiredRule("partner_code", "Mã khách hàng"));
        RegisterRule("PARTNERS", new RequiredRule("partner_name", "Tên khách hàng"));
        RegisterRule("PARTNERS", new MaxLengthRule("partner_code", "Mã khách hàng", 50));
        RegisterRule("PARTNERS", new MaxLengthRule("partner_name", "Tên khách hàng", 255));
        RegisterRule("PARTNERS", new PhoneRule());
        RegisterRule("PARTNERS", new EmailRule());
        RegisterRule("PARTNERS", new EnumRule("partner_type", "Loại khách hàng", new[] { "STORE", "ASO", "CORPORATE", "WAREHOUSE" }));
        RegisterRule("PARTNERS", new EnumRule("segment", "Phân khúc", new[] { "A", "B", "C", "D", "E" }));
        RegisterRule("PARTNERS", new DecimalRule("credit_limit", "Hạn mức", 0, 999999999999999.99m));

        // ========== PRODUCTS ==========
        RegisterRule("PRODUCTS", new RequiredRule("sku", "SKU"));
        RegisterRule("PRODUCTS", new RequiredRule("product_name", "Tên sản phẩm"));
        RegisterRule("PRODUCTS", new MaxLengthRule("sku", "SKU", 50));
        RegisterRule("PRODUCTS", new MaxLengthRule("product_name", "Tên sản phẩm", 255));
        RegisterRule("PRODUCTS", new DecimalRule("selling_price", "Giá bán", 0, 999999999999.99m));
        RegisterRule("PRODUCTS", new DecimalRule("vat_rate", "VAT%", 0, 100));
        RegisterRule("PRODUCTS", new DecimalRule("weight_per_unit", "Trọng lượng", 0, 999999.99m));
        RegisterRule("PRODUCTS", new DecimalRule("pack_size_ml", "Thể tích", 0, 999999.99m));

        // ========== ORDERS ==========
        RegisterRule("ORDERS", new RequiredRule("so_number", "Số đơn hàng"));
        RegisterRule("ORDERS", new RequiredRule("customer_id", "Mã khách hàng"));
        RegisterRule("ORDERS", new DateRule("order_date", "Ngày đặt hàng"));
        RegisterRule("ORDERS", new DateRule("delivered_date", "Ngày giao hàng"));
        RegisterRule("ORDERS", new EnumRule("status", "Trạng thái", new[] { "DRAFT", "CONFIRMED", "SHIPPED", "DELIVERED", "CANCELLED" }));
        RegisterRule("ORDERS", new EnumRule("payment_status", "TT thanh toán", new[] { "UNPAID", "PARTIAL", "PAID", "OVERDUE" }));
        RegisterRule("ORDERS", new DecimalRule("total_amount", "Tổng tiền", 0, 99999999999999.99m));
    }

    private void RegisterDefaultConverters()
    {
        _converters["string"] = v => v;
        _converters["int"] = v => int.TryParse(v, out var i) ? i : null;
        _converters["decimal"] = v => decimal.TryParse(v, out var d) ? d : null;
        _converters["date"] = v => DateTime.TryParse(v, out var dt) ? dt : null;
        _converters["bool"] = v => bool.TryParse(v, out var b) ? b : (v == "1" || v.ToLower() == "true" || v.ToLower() == "yes");
    }

    private bool TryConvert(string? value, string dataType, string fieldName, out object? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        var converter = _converters.GetValueOrDefault(dataType.ToLower()) ?? _converters["string"];
        result = converter(value);
        return result != null || string.IsNullOrEmpty(value);
    }
}

// ============ Validation Rules ============
public interface IValidationRule
{
    string FieldName { get; }
    string? DisplayName { get; }
    bool IsRequired { get; }
    string DataType { get; }
    ValidationRuleResult Validate(object? value, string rawValue);
}

public class ValidationRuleResult
{
    public bool IsValid { get; set; } = true;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuggestedFix { get; set; }
    public bool HasWarning { get; set; }
    public string? WarningCode { get; set; }
    public string? WarningMessage { get; set; }

    public static ValidationRuleResult Success() => new() { IsValid = true };
    public static ValidationRuleResult Error(string code, string message, string? fix = null)
        => new() { IsValid = false, ErrorCode = code, ErrorMessage = message, SuggestedFix = fix };
    public static ValidationRuleResult Warning(string code, string message, string? fix = null)
        => new() { IsValid = true, HasWarning = true, WarningCode = code, WarningMessage = message, SuggestedFix = fix };
}

public class RequiredRule : IValidationRule
{
    public string FieldName { get; }
    public string? DisplayName { get; }
    public bool IsRequired { get; } = true;
    public string DataType => "string";

    public RequiredRule(string field, string? display = null)
    {
        FieldName = field;
        DisplayName = display ?? field;
    }

    public ValidationRuleResult Validate(object? value, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return ValidationRuleResult.Error("REQUIRED", $"{DisplayName} không được để trống");
        return ValidationRuleResult.Success();
    }
}

public class MaxLengthRule : IValidationRule
{
    public string FieldName { get; }
    public string? DisplayName { get; }
    public bool IsRequired => false;
    public string DataType => "string";
    public int MaxLength { get; }

    public MaxLengthRule(string field, string? display, int maxLen)
    {
        FieldName = field;
        DisplayName = display ?? field;
        MaxLength = maxLen;
    }

    public ValidationRuleResult Validate(object? value, string rawValue)
    {
        if (rawValue?.Length > MaxLength)
        {
            var trimmed = rawValue.Substring(0, MaxLength);
            return ValidationRuleResult.Warning("MAX_LENGTH",
                $"{DisplayName} dài hơn {MaxLength} ký tự, đã cắt ngắn",
                trimmed);
        }
        return ValidationRuleResult.Success();
    }
}

public class PhoneRule : IValidationRule
{
    public string FieldName { get; } = "phone";
    public string? DisplayName { get; } = "Số điện thoại";
    public bool IsRequired => false;
    public string DataType => "string";
    private static readonly Regex PhoneRegex = new(@"^0[0-9]{9,10}$", RegexOptions.Compiled);

    public ValidationRuleResult Validate(object? value, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return ValidationRuleResult.Success();
        var cleaned = Regex.Replace(rawValue, @"[.\-\s]", "");
        if (!PhoneRegex.IsMatch(cleaned))
        {
            return ValidationRuleResult.Warning("INVALID_PHONE",
                $"Số điện thoại '{rawValue}' không đúng định dạng",
                cleaned.Length >= 10 ? cleaned.Substring(0, 10) : cleaned);
        }
        return ValidationRuleResult.Success();
    }
}

public class EmailRule : IValidationRule
{
    public string FieldName { get; } = "email";
    public string? DisplayName { get; } = "Email";
    public bool IsRequired => false;
    public string DataType => "string";
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ValidationRuleResult Validate(object? value, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return ValidationRuleResult.Success();
        if (!EmailRegex.IsMatch(rawValue))
        {
            return ValidationRuleResult.Warning("INVALID_EMAIL",
                $"Email '{rawValue}' không đúng định dạng");
        }
        return ValidationRuleResult.Success();
    }
}

public class EnumRule : IValidationRule
{
    public string FieldName { get; }
    public string? DisplayName { get; }
    public bool IsRequired { get; }
    public string DataType => "string";
    private readonly HashSet<string> _validValues;

    public EnumRule(string field, string? display, string[] validValues, bool required = false)
    {
        FieldName = field;
        DisplayName = display ?? field;
        IsRequired = required;
        _validValues = new HashSet<string>(validValues, StringComparer.OrdinalIgnoreCase);
    }

    public ValidationRuleResult Validate(object? value, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return ValidationRuleResult.Success();
        if (!_validValues.Contains(rawValue))
        {
            return ValidationRuleResult.Error("INVALID_ENUM",
                $"{DisplayName} phải là: {string.Join(", ", _validValues)}");
        }
        return ValidationRuleResult.Success();
    }
}

public class DecimalRule : IValidationRule
{
    public string FieldName { get; }
    public string? DisplayName { get; }
    public bool IsRequired => false;
    public string DataType => "decimal";
    public decimal Min { get; }
    public decimal Max { get; }

    public DecimalRule(string field, string? display, decimal min, decimal max)
    {
        FieldName = field;
        DisplayName = display ?? field;
        Min = min;
        Max = max;
    }

    public ValidationRuleResult Validate(object? value, string rawValue)
    {
        if (value is not decimal d) return ValidationRuleResult.Success();
        if (d < Min || d > Max)
        {
            var clamped = Math.Max(Min, Math.Min(Max, d));
            return ValidationRuleResult.Warning("DECIMAL_RANGE",
                $"{DisplayName} = {d} nằm ngoài phạm vi [{Min}, {Max}], đã clamp về {clamped}",
                clamped.ToString());
        }
        return ValidationRuleResult.Success();
    }
}

public class DateRule : IValidationRule
{
    public string FieldName { get; }
    public string? DisplayName { get; }
    public bool IsRequired { get; }
    public string DataType => "date";

    public DateRule(string field, string? display, bool required = false)
    {
        FieldName = field;
        DisplayName = display ?? field;
        IsRequired = required;
    }

    public ValidationRuleResult Validate(object? value, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return ValidationRuleResult.Success();
        if (value is not DateTime dt)
            return ValidationRuleResult.Error("INVALID_DATE", $"{DisplayName} không đúng định dạng ngày");
        if (dt < new DateTime(2000, 1, 1) || dt > DateTime.UtcNow.AddYears(2))
            return ValidationRuleResult.Warning("DATE_RANGE", $"{DisplayName} = {dt:yyyy-MM-dd} có thể không hợp lệ");
        return ValidationRuleResult.Success();
    }
}
