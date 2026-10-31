using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ApiDump
{
    /// <summary>
    /// Dumps the Dynamo-visible API of PlotterNodes with Dynamo's mangled function signatures.
    /// Mirrors Dynamo's FunctionDescriptor.MangledName: QualifiedName + "@" + parameter DS types
    /// (instance methods exclude "this"; no "@" when there are no parameters).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var output = args.Length > 0 ? args[0] : "plotter_api.json";
            var asm = typeof(Plotter.Shape).Assembly;
            var functions = new SortedDictionary<string, List<object>>(StringComparer.Ordinal);

            foreach (var type in asm.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (IsHidden(type)) continue;
                var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (var m in type.GetMethods(flags))
                {
                    if (m.IsSpecialName || IsHidden(m) || m.Name == "ToString" || m.Name == "Equals" || m.Name == "GetHashCode") continue;
                    var parameters = m.GetParameters().Select(p => new Dictionary<string, object>
                    {
                        ["name"] = p.Name,
                        ["ds"] = DsType(p.ParameterType, p.GetCustomAttributes().Any(a => a.GetType().Name == "ArbitraryDimensionArrayImportAttribute")),
                        ["cs"] = CsType(p.ParameterType),
                        ["hasDefault"] = p.HasDefaultValue || p.GetCustomAttributes().Any(a => a.GetType().Name == "DefaultArgumentAttribute"),
                        ["default"] = p.HasDefaultValue ? FormatDefault(p.DefaultValue) : null,
                    }).ToList();

                    var qualified = type.FullName + "." + m.Name;
                    var mangled = parameters.Count > 0 ? qualified + "@" + string.Join(",", parameters.Select(p => p["ds"])) : qualified;
                    var multi = m.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "MultiReturnAttribute");
                    var keys = multi == null ? null : (string[])multi.GetType().GetProperty("ReturnKeys").GetValue(multi);
                    var retArbitrary = m.ReturnParameter.GetCustomAttributes().Any(a => a.GetType().Name == "ArbitraryDimensionArrayImportAttribute");

                    if (!functions.TryGetValue(qualified, out var list)) functions[qualified] = list = new List<object>();
                    list.Add(new Dictionary<string, object>
                    {
                        ["signature"] = mangled,
                        ["static"] = m.IsStatic,
                        ["class"] = type.FullName,
                        ["method"] = m.Name,
                        ["thisName"] = m.IsStatic ? null : char.ToLowerInvariant(type.Name[0]) + type.Name.Substring(1),
                        ["params"] = parameters,
                        ["returnDs"] = DsType(m.ReturnType, retArbitrary),
                        ["returnCs"] = CsType(m.ReturnType),
                        ["multiReturn"] = keys,
                    });
                }
            }

            var json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["assembly"] = asm.GetName().FullName,
                ["functions"] = functions,
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(output, json);
            Console.WriteLine($"wrote {output}: {functions.Count} functions");
            return 0;
        }

        private static bool IsHidden(MemberInfo m)
        {
            foreach (var a in m.GetCustomAttributes())
            {
                if (a.GetType().Name != "IsVisibleInDynamoLibraryAttribute") continue;
                var visible = (bool)a.GetType().GetProperty("Visible").GetValue(a);
                if (!visible) return true;
            }
            return false;
        }

        /// <summary>DesignScript type name as ProtoCore.Type.ToString() renders it.</summary>
        private static string DsType(Type t, bool arbitrary)
        {
            if (arbitrary) return "var[]..[]";
            var rank = 0;
            while (true)
            {
                var elem = ElementType(t);
                if (elem == null) break;
                rank++;
                t = elem;
            }
            string name;
            if (t == typeof(double) || t == typeof(float)) name = "double";
            else if (t == typeof(int) || t == typeof(long) || t == typeof(short)) name = "int";
            else if (t == typeof(bool)) name = "bool";
            else if (t == typeof(string)) name = "string";
            else if (t == typeof(object) || t == typeof(void)) name = "var";
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) name = "var";
            else name = t.FullName;
            return name + string.Concat(Enumerable.Repeat("[]", rank));
        }

        private static Type ElementType(Type t)
        {
            if (t == typeof(string)) return null;
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(IList<>) || def == typeof(List<>) || def == typeof(IEnumerable<>) || def == typeof(ICollection<>) || def == typeof(IReadOnlyList<>))
                    return t.GetGenericArguments()[0];
            }
            return null;
        }

        private static string CsType(Type t)
        {
            if (t == typeof(void)) return "void";
            if (t.IsArray) return CsType(t.GetElementType()) + "[]";
            if (t.IsGenericType)
            {
                var name = t.GetGenericTypeDefinition().FullName;
                name = name.Substring(0, name.IndexOf('`'));
                return name + "<" + string.Join(", ", t.GetGenericArguments().Select(CsType)) + ">";
            }
            return t.FullName;
        }

        private static object FormatDefault(object value)
        {
            switch (value)
            {
                case null: return null;
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case bool b: return b ? "true" : "false";
                case string s: return "\"" + s + "\"";
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
