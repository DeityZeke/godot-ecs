
using System;
using System.IO;
using System.Numerics;

namespace UltraSim.IO
{
    public sealed class BinaryFileReader : IReader
    {
        private readonly BinaryReader _reader;

        public BinaryFileReader(string path)
        {
            _reader = new BinaryReader(File.OpenRead(path));
        }

        public bool ReadBool() => _reader.ReadBoolean();
        public byte ReadByte() => _reader.ReadByte();
        public short ReadInt16() => _reader.ReadInt16();
        public int ReadInt32() => _reader.ReadInt32();
        public long ReadInt64() => _reader.ReadInt64();
        public float ReadSingle() => _reader.ReadSingle();
        public double ReadDouble() => _reader.ReadDouble();
        public string ReadString() => _reader.ReadString();
        public Vector3 ReadVector3() => new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
        public Quaternion ReadQuaternion() => new Quaternion(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
        public byte[] ReadBytes(int count) => _reader.ReadBytes(count);

        public void Dispose() => _reader.Dispose();
    }
}
