
using System;
using System.IO;
using System.Numerics;

namespace UltraSim.IO
{
    public sealed class BinaryFileWriter : IWriter
    {
        private readonly BinaryWriter _writer;

        public BinaryFileWriter(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            _writer = new BinaryWriter(File.OpenWrite(path));
        }

        public void Write(bool v) => _writer.Write(v);
        public void Write(byte v) => _writer.Write(v);
        public void Write(short v) => _writer.Write(v);
        public void Write(int v) => _writer.Write(v);
        public void Write(long v) => _writer.Write(v);
        public void Write(float v) => _writer.Write(v);
        public void Write(double v) => _writer.Write(v);
        public void Write(string v) => _writer.Write(v ?? "");
        public void Write(Vector3 v) { Write(v.X); Write(v.Y); Write(v.Z); }
        public void Write(Quaternion v) { Write(v.X); Write(v.Y); Write(v.Z); Write(v.W); }
        public void Write(byte[] data, int offset, int count) => _writer.Write(data, offset, count);

        public void Dispose() => _writer.Dispose();
    }
}
