using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeSaveJson
    {
        sealed class Number { public string text; }
        public static T FromJson<T>(string json)
        {
            var syntax = new Parser(json).Read();
            object value = JsonUtility.FromJson<T>(json);
            return (T)Restore(value, typeof(T), syntax);
        }
        static object Restore(object value, Type type, object syntax)
        {
            if (type == typeof(double))
            {
                if (!(syntax is Number number)) throw new FormatException("Double field requires a JSON number");
                // Unity's JSON numeric conversion can round one ULP away from the serialized decimal.
                return double.Parse(number.text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            if (value == null || syntax == null || type == typeof(string) || type.IsPrimitive || type.IsEnum) return value;
            if (type.IsArray && value is Array array && syntax is List<object> elements)
            {
                if (array.Length != elements.Count) throw new FormatException("JSON array length differs from DTO");
                Type elementType = type.GetElementType();
                for (int i = 0; i < array.Length; i++) array.SetValue(Restore(array.GetValue(i), elementType, elements[i]), i);
            }
            else if (value is IList list && type.IsGenericType && syntax is List<object> items)
            {
                if (list.Count != items.Count) throw new FormatException("JSON list length differs from DTO");
                Type elementType = type.GetGenericArguments()[0];
                for (int i = 0; i < list.Count; i++) list[i] = Restore(list[i], elementType, items[i]);
            }
            else if (syntax is Dictionary<string, object> fields)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (!field.IsInitOnly && fields.TryGetValue(field.Name, out object token))
                        field.SetValue(value, Restore(field.GetValue(value), field.FieldType, token));
            }
            return value;
        }
        sealed class Parser
        {
            readonly string text;
            int index;
            public Parser(string json) { text = json ?? throw new ArgumentNullException(nameof(json)); }
            public object Read()
            {
                object result = Value(0); Space();
                if (index != text.Length) throw new FormatException("Trailing JSON content");
                return result;
            }
            void Space() { while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || text[index] == '\r' || text[index] == '\n')) index++; }
            bool Take(char value) { Space(); if (index >= text.Length || text[index] != value) return false; index++; return true; }
            void Require(char value) { if (!Take(value)) throw new FormatException("Expected JSON delimiter " + value); }
            object Value(int depth)
            {
                if (depth > 128) throw new FormatException("JSON nesting limit exceeded");
                Space(); if (index >= text.Length) throw new FormatException("Missing JSON value");
                char next = text[index];
                if (next == '{')
                {
                    index++; var fields = new Dictionary<string, object>(StringComparer.Ordinal);
                    if (Take('}')) return fields;
                    do { string key = String(); Require(':'); if (fields.ContainsKey(key)) throw new FormatException("Duplicate JSON property"); fields.Add(key, Value(depth + 1)); } while (Take(','));
                    Require('}'); return fields;
                }
                if (next == '[')
                {
                    index++; var values = new List<object>(); if (Take(']')) return values;
                    do { values.Add(Value(depth + 1)); } while (Take(',')); Require(']'); return values;
                }
                if (next == '"') return String();
                if (next == 'n') { Literal("null"); return null; }
                if (next == 't') { Literal("true"); return true; }
                if (next == 'f') { Literal("false"); return false; }
                int start = index;
                if (text[index] == '-') index++;
                if (index >= text.Length) throw new FormatException("Incomplete JSON number");
                if (text[index] == '0') index++;
                else { if (text[index] < '1' || text[index] > '9') throw new FormatException("Invalid JSON value"); Digits(); }
                if (index < text.Length && text[index] == '.') { index++; Digits(true); }
                if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
                {
                    index++; if (index < text.Length && (text[index] == '+' || text[index] == '-')) index++; Digits(true);
                }
                return new Number { text = text.Substring(start, index - start) };
            }
            void Digits(bool required = false)
            {
                int start = index; while (index < text.Length && text[index] >= '0' && text[index] <= '9') index++;
                if (required && index == start) throw new FormatException("Missing JSON digits");
            }
            void Literal(string value)
            {
                if (index + value.Length > text.Length || string.CompareOrdinal(text, index, value, 0, value.Length) != 0) throw new FormatException("Invalid JSON literal");
                index += value.Length;
            }
            string String()
            {
                Require('"'); var result = new StringBuilder();
                while (index < text.Length)
                {
                    char value = text[index++]; if (value == '"') return result.ToString();
                    if (value < 32) throw new FormatException("Invalid JSON string control");
                    if (value != '\\') { result.Append(value); continue; }
                    if (index >= text.Length) throw new FormatException("Incomplete JSON escape");
                    switch (text[index++])
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (index + 4 > text.Length) throw new FormatException("Incomplete Unicode escape");
                            result.Append((char)ushort.Parse(text.Substring(index, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)); index += 4; break;
                        default: throw new FormatException("Invalid JSON escape");
                    }
                }
                throw new FormatException("Unterminated JSON string");
            }
        }
    }
}
