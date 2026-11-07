using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LanShare.Tests
{
    /// <summary>
    /// Flujo de solo lectura que entrega como mucho N bytes por lectura.
    ///
    /// Una red real trocea los datos de forma arbitraria; con trozos de 1, 2 o
    /// 7 bytes el delimitador multipart cae partido entre lecturas en todas
    /// las posiciones posibles, que es exactamente lo que hay que probar.
    /// </summary>
    public sealed class ChunkedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunk;
        private int _position;

        public ChunkedStream(byte[] data, int chunk)
        {
            _data = data;
            _chunk = Math.Max(1, chunk);
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _data.Length; } }

        public override long Position
        {
            get { return _position; }
            set { throw new NotSupportedException(); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, _chunk), _data.Length - _position);
            if (n <= 0) return 0;
            Buffer.BlockCopy(_data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
}
