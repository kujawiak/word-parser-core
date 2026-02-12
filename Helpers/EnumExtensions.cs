using System;
using System.Reflection;

[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
sealed class EnumDescriptionAttribute : Attribute
{
    public string Description { get; }

    public EnumDescriptionAttribute(string description)
    {
        Description = description;
    }
}

public static class EnumExtensions
{
    public static string ToDescription(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attribute = field?.GetCustomAttribute(typeof(EnumDescriptionAttribute)) as EnumDescriptionAttribute;
        return attribute?.Description ?? value.ToString();
    }
}