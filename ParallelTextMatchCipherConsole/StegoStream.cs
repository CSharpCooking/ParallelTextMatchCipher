using System;
using System.IO;

namespace Stego
{
    public class StegoStream : Stream
    {
        private Stream _stream;
        private StegoAlg _stegoAlg;

        public StegoStream(Stream stream, StegoAlg stegoAlg)
        {
            _stream = stream;
            _stegoAlg = stegoAlg;
        }

        public override bool CanRead => throw new NotImplementedException();

        public override bool CanSeek => throw new NotImplementedException();

        public override bool CanWrite => throw new NotImplementedException();

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            for (int i = offset; i < count; i++)
            {
                var hiddenCode = new byte[_stegoAlg.HiddenCodeLength];
                _stream.Read(hiddenCode, 0, hiddenCode.Length);

                _stegoAlg.TryDisclose(hiddenCode, out buffer[i]);
            }

            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++)
            {
                var hiddenCode = _stegoAlg.Hide(buffer[i]);
                _stream.Write(hiddenCode, 0, hiddenCode.Length);
            }
        }
    }
}
