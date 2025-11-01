
using System;
using System.Numerics;

using UltraSim.Configuration;

namespace UltraSim.IO
{
    /// <summary>
    /// Writer that wraps ConfigFile for compatibility with IWriter interface.
    /// Writes values to a default section for simple use cases.
    /// For advanced section-based writing, use ConfigFile directly.
    /// </summary>
    public class ConfigFileWriter : IWriter
    {
        private readonly ConfigFile _config;
        private readonly string _path;
        private readonly string _section;
        private int _keyIndex = 0;

        public ConfigFileWriter(string path, string section = "Data")
        {
            _config = new ConfigFile();
            _path = path;
            _section = section;
        }

        /// <summary>
        /// Gets the underlying ConfigFile for advanced usage.
        /// </summary>
        public ConfigFile ConfigFile => _config;

        private string NextKey() => $"_{_keyIndex++}";

        public void Write(bool v) => _config.SetValue(_section, NextKey(), v);
        public void Write(byte v) => _config.SetValue(_section, NextKey(), v);
        public void Write(short v) => _config.SetValue(_section, NextKey(), v);
        public void Write(int v) => _config.SetValue(_section, NextKey(), v);
        public void Write(long v) => _config.SetValue(_section, NextKey(), v);
        public void Write(float v) => _config.SetValue(_section, NextKey(), v);
        public void Write(double v) => _config.SetValue(_section, NextKey(), v);
        public void Write(string v) => _config.SetValue(_section, NextKey(), v);

        public void Write(Vector3 v)
        {
            Write(v.X);
            Write(v.Y);
            Write(v.Z);
        }

        public void Write(Quaternion v)
        {
            Write(v.X);
            Write(v.Y);
            Write(v.Z);
            Write(v.W);
        }

        public void Write(byte[] data, int offset, int count)
        {
            // Store as base64 string
            byte[] slice = new byte[count];
            Array.Copy(data, offset, slice, 0, count);
            _config.SetValue(_section, NextKey(), Convert.ToBase64String(slice));
        }

        public void Dispose()
        {
            _config.Save(_path);
        }
    }
}
