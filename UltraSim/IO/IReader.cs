using System;
using System.Numerics;

namespace UltraSim.IO
{
    public interface IReader : IDisposable
    {
        bool ReadBool();
        byte ReadByte();
        short ReadInt16();
        int ReadInt32();
        long ReadInt64();
        float ReadSingle();
        double ReadDouble();
        string ReadString();
        Vector3 ReadVector3();
        Quaternion ReadQuaternion();
        byte[] ReadBytes(int count);
    }
}
