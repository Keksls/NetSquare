using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace NetSquare.Core.Configuration
{
    /// <summary>
    /// Provides JSON serialization through framework-provided APIs only.
    /// </summary>
    public static class NetSquareJsonSerializer
    {
        /// <summary>
        /// Serializes a value to JSON.
        /// </summary>
        /// <typeparam name="T">Compile-time value type.</typeparam>
        /// <param name="value">Value to serialize.</param>
        /// <returns>Serialized JSON.</returns>
        public static string Serialize<T>(T value)
        {
            // Use the compile-time type for ordinary NetSquare payloads.
            return Serialize(value, typeof(T));
        }

        /// <summary>
        /// Serializes a value to JSON with an explicitly selected contract type.
        /// </summary>
        /// <param name="value">Value to serialize.</param>
        /// <param name="type">Runtime contract type.</param>
        /// <returns>Serialized JSON.</returns>
        public static string Serialize(object value, Type type)
        {
            // The explicit type preserves properties added by project-defined configuration classes.
            using (MemoryStream stream = new MemoryStream())
            {
                CreateSerializer(type).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>
        /// Deserializes JSON to a value.
        /// </summary>
        /// <typeparam name="T">Expected value type.</typeparam>
        /// <param name="json">JSON payload.</param>
        /// <returns>Deserialized value.</returns>
        public static T Deserialize<T>(string json)
        {
            // Use the compile-time type for ordinary NetSquare payloads.
            object value = Deserialize(json, typeof(T));
            return value == null ? default(T) : (T)value;
        }

        /// <summary>
        /// Deserializes JSON with an explicitly selected contract type.
        /// </summary>
        /// <param name="json">JSON payload.</param>
        /// <param name="type">Runtime contract type.</param>
        /// <returns>Deserialized value.</returns>
        public static object Deserialize(string json, Type type)
        {
            // The explicit type creates the concrete configuration selected by the consuming project.
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (MemoryStream stream = new MemoryStream(bytes))
                return CreateSerializer(type).ReadObject(stream);
        }

        /// <summary>
        /// Creates the framework JSON serializer for the requested contract type.
        /// </summary>
        /// <param name="type">Runtime contract type.</param>
        /// <returns>Configured JSON serializer.</returns>
        private static DataContractJsonSerializer CreateSerializer(Type type)
        {
            // Centralize serializer construction so generic and runtime-type paths behave identically.
            return new DataContractJsonSerializer(type);
        }
    }
}
