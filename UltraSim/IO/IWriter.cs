
using System;
using System.Numerics;

namespace UltraSim.IO
{
    public interface IWriter : IDisposable
    {
        void Write(bool v);
        void Write(byte v);
        void Write(short v);
        void Write(int v);
        void Write(long v);
        void Write(float v);
        void Write(double v);
        void Write(string v);
        void Write(Vector3 v);
        void Write(Quaternion v);
        void Write(byte[] data, int offset, int count);
    }
}
