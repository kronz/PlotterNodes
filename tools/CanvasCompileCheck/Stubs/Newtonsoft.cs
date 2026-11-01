// Json.NET 13 attributes used by PlotterNodes.Canvas. Stub bodies only; see ../README.md.
#pragma warning disable CS1591
using System;

namespace Newtonsoft.Json
{
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false)]
    public sealed class JsonConstructorAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class JsonConverterAttribute : Attribute
    {
        public JsonConverterAttribute(Type converterType) { }
    }

    public abstract class JsonConverter { }

    public class JsonException : Exception { }

    public static class JsonConvert
    {
        public static string SerializeObject(object value) { throw null; }
        public static T DeserializeObject<T>(string value) { throw null; }
    }
}

namespace Newtonsoft.Json.Converters
{
    public class StringEnumConverter : Newtonsoft.Json.JsonConverter { }
}
