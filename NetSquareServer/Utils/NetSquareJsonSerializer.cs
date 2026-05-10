using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

#region Source
namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Provides JSON serialization through framework-provided APIs only.
    /// </summary>
    internal static class NetSquareJsonSerializer
    {
        /// <summary>
        /// Serializes a value to JSON.
        /// </summary>
        public static string Serialize<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                CreateSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>
        /// Deserializes JSON to a value.
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (MemoryStream stream = new MemoryStream(bytes))
            {
                object value = CreateSerializer(typeof(T)).ReadObject(stream);
                return value == null ? default(T) : (T)value;
            }
        }

        private static DataContractJsonSerializer CreateSerializer(Type type)
        {
            return new DataContractJsonSerializer(type);
        }
    }
}
#endregion
