
using System;
using System.Numerics;

using UltraSim.Configuration;

namespace UltraSim.IO
{
    /// <summary>
    /// Reader that wraps ConfigFile for compatibility with IReader interface.
    /// Reads values from a default section for simple use cases.
    /// For advanced section-based reading, use ConfigFile directly.
    /// </summary>
    public class ConfigFileReader : IReader
    {
        private readonly ConfigFile _config;
        private readonly string _section;
        private int _keyIndex = 0;

        public ConfigFileReader(string path, string section = "Data")
        {
            _config = new ConfigFile();
            _config.Load(path);
            _section = section;
        }

        /// <summary>
        /// Gets the underlying ConfigFile for advanced usage.
        /// </summary>
        public ConfigFile ConfigFile => _config;

        private string NextKey() => $"_{_keyIndex++}";

        public bool ReadBool() => _config.GetValue(_section, NextKey(), false);
        public byte ReadByte() => _config.GetValue(_section, NextKey(), (byte)0);
        public short ReadInt16() => (short)_config.GetValue(_section, NextKey(), 0);
        public int ReadInt32() => _config.GetValue(_section, NextKey(), 0);
        public long ReadInt64() => (long)_config.GetValue(_section, NextKey(), 0L);
        public float ReadSingle() => _config.GetValue(_section, NextKey(), 0f);
        public double ReadDouble() => _config.GetValue(_section, NextKey(), 0.0);
        public string ReadString() => _config.GetValue(_section, NextKey(), string.Empty);

        public Vector3 ReadVector3()
        {
            return new Vector3(
                ReadSingle(),
                ReadSingle(),
                ReadSingle()
            );
        }

        public Quaternion ReadQuaternion()
        {
            return new Quaternion(
                ReadSingle(),
                ReadSingle(),
                ReadSingle(),
                ReadSingle()
            );
        }

        public byte[] ReadBytes(int count)
        {
            string base64 = ReadString();
            try
            {
                return Convert.FromBase64String(base64);
            }
            catch
            {
                return new byte[count];
            }
        }

        public void Dispose()
        {
            // Nothing to dispose for reader
        }
    }
}
