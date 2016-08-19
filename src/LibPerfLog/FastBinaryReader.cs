using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.IO;

namespace PerformanceLog {

  public unsafe class FastBinaryReader :IDisposable {
    private byte[] _buffer;
    private GCHandle _bufHandle;
    private byte* _buffBase;
    private byte* _buffPtr, _buffLimit;
    private long _totalRead;
    private Stream _stream;

    public FastBinaryReader() {
      _buffer = new byte[1024 * 1024];
      _bufHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
      _buffBase = (byte*)_bufHandle.AddrOfPinnedObject();
      _buffPtr = _buffLimit = _buffBase;
    }

    public FastBinaryReader(Stream stream)
      : this(){
      _stream = stream;
    }

    public FastBinaryReader(string filePath)
      : this() {
      _stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public Stream BaseStream => _stream;
    public int BufferOffset => (int)(_buffPtr - _buffBase);
    public int BufferBytesRemaining => (int)(_buffLimit - _buffPtr);
    public long TotalRead => _totalRead + (long)BufferOffset;

    public byte[] ReadBytes(int count) {
      var result = new byte[count];

      if (BufferHasBytes(count)) {
        Buffer.BlockCopy(_buffer, BufferOffset, result, 0, count);
        _buffPtr += count;
      } else {
        int bytesCopied = BufferBytesRemaining;
        ReadBytes(result, 0, bytesCopied);
        int extra = count - bytesCopied;
        FillBuffer(extra);
        /* Call ourselves the earlier FillBuffer call should avoid stack overflow*/
        ReadBytes(result, bytesCopied, extra);
      }

      return result;
    }

    public int ReadBytes(byte[] result, int offset, int count) {

      if (BufferHasBytes(count)) {
        Buffer.BlockCopy(_buffer, BufferOffset, result, offset, count);
        _buffPtr += count;
        return count;
      } else {
        int bytesCopied = BufferBytesRemaining;
        Buffer.BlockCopy(_buffer, BufferOffset, result, offset, bytesCopied);
        _buffPtr = _buffLimit;

        int extra = count - bytesCopied;

        if (extra < _buffer.Length) {
          FillBuffer(extra);
          ReadBytes(result, bytesCopied, extra);
          return extra;
        } else {
          int read = _stream.Read(result, bytesCopied, extra);
          this._totalRead += read;
          return read;
        }
      }
    }

    public void SkipBytes(int count) {
      if (BufferBytesRemaining < count) {
        FillBuffer(count);
      }
      _buffPtr += count;
    }

    public int ReadInt32() {
      const int size = sizeof(int);

      if ((_buffLimit - _buffPtr) < size) {
        FillBuffer(size);
      }
      var result = *(int*)_buffPtr;
      _buffPtr += size;

      return result;
    }

    public uint ReadUInt32() {
      const int size = sizeof(uint);

      if ((_buffLimit - _buffPtr) < size) {
        FillBuffer(size);
      }
      var result = *(uint*)_buffPtr;
      _buffPtr += size;

      return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte() {
      const int size = sizeof(byte);

      if ((_buffLimit - _buffPtr) < size) {
        FillBuffer(size);
      }
      var result = *_buffPtr;
      _buffPtr += size;

      return result;
    }

    public short ReadInt16() {
      const int size = sizeof(short);

      if ((_buffLimit - _buffPtr) < size) {
        FillBuffer(size);
      }
      var result = *(short*)_buffPtr;
      _buffPtr += size;

      return result;
    }

    public ushort ReadUInt16() {
      const int size = sizeof(ushort);

      if ((_buffLimit - _buffPtr) < size) {
        FillBuffer(size);
      }
      var result = *(ushort*)_buffPtr;
      _buffPtr += size;

      return result;
    }

    public long ReadInt64() {
      const int size = sizeof(long);

      if ((_buffLimit - _buffPtr) < size) {
        FillBuffer(size);
      }
      var result = *(long*)_buffPtr;
      _buffPtr += size;

      return result;
    }

    public ulong ReadUInt64() {
      const int size = sizeof(ulong);

      if ((_buffLimit - _buffPtr) < size) {
        FillBuffer(size);
      }
      var result = *(ulong*)_buffPtr;
      _buffPtr += size;

      return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint readVarInt() {
      unchecked {
        if (BufferHasBytes(4)) {
          byte v = *_buffPtr++;
          uint result = (v & 0x7Fu);

          int shift = 0;
          while ((v & 0x80u) != 0) {
            v = *_buffPtr++;
            shift += 7;
            result += (v & 0x7Fu) << shift;
          }

          return result;
        } else {
          return ReadVarIntSlow();
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong readVarInt64() {
      unchecked {
        if (BufferHasBytes(8)) {
          byte v = *_buffPtr++;
          ulong result = (v & 0x7Fu);

          int shift = 0;
          while ((v & 0x80u) != 0) {
            v = *_buffPtr++;
            shift += 7;
            result += (v & 0x7Fu) << shift;
          }

          return result;
        } else {
          return ReadVarIntSlow();
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SkipVarInt() {
      unchecked {
        if (BufferHasBytes(4)) {
          int v = *_buffPtr++;

          while ((v & 0x80u) != 0) {
            v = *_buffPtr++;
          }
          /*
          byte v = *_buffPtr++;
          int bit = (v >> 7);
          int bit2 = (_buffPtr[1] >> 7);
          int bit3 = (_buffPtr[2] >> 7);

          (bit & bit2) + (bit
         */
        } else {
          ReadVarIntSlow();
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadPPId(byte typeByte) {
      unchecked {
        if (BufferHasBytes(2)) {
          var top = 0;
          var mid = typeByte & 0xF;
          var bot = *_buffPtr++;

          if ((typeByte & 0x10) != 0) {
            top = *_buffPtr++;
          }

          return (ushort)((top << 12) + (mid << 8) + bot);
        } else {
          return ReadPPIdSlow(typeByte);
        }
      }
    }

    public ushort ReadPPIdSlow(byte typeByte) {
      unchecked {
          var top = 0;
          var mid = typeByte & 0xF;
          var bot = ReadByte();

          if ((typeByte & 0x10) != 0) {
            top = ReadByte();
          }

          return (ushort)((top << 12) + (mid << 8) + bot);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public uint ReadVarIntSlow() {
      unchecked {
        byte v = ReadByte();
        uint result = (v & 0x7Fu);

        int shift = 0;
        while ((v & 0x80u) != 0) {
          v = ReadByte();
          shift += 7;
          result += (v & 0x7Fu) << shift;
        }

        return result;
      }
    }

    public string ReadString(Encoding encoding, int length)
    {
      if (BufferHasBytes(length)) {
        var result = encoding.GetString(_buffer, BufferOffset, length);
        _buffPtr += length;
        return result;
      } else {
        return encoding.GetString(ReadBytes(length));
      }
    }

    public bool BufferHasBytes(int nbytes) {
      return (_buffLimit - _buffPtr) >= nbytes;
    }

    private void SetBufferOffset(int offset) {
      Debug.Assert(offset >= 0 && (_buffPtr + offset) < _buffLimit);
      _buffPtr = _buffBase + offset;
      //_totalRead += (long)offset;
    }

    Task<ReadOffset> reader;

    struct ReadOffset {
      public int Offset;
      public int Read;

      public ReadOffset(int offset, int read) {
        Offset = offset;
        Read = read;
      }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FillBuffer(int minBytes) {
      _totalRead += BufferOffset;
      int read = _stream.Read(_buffer, 0, _buffer.Length);

      if (read < minBytes) {
        throw new Exception($"Not enough bytes read from the stream. Expected minimum of {minBytes} but got {read}");
      }

      _buffPtr = _buffBase;
      _buffLimit = _buffPtr + read;
    }

    private Task<ReadOffset> ReadAhead(int baseOffset, int size) {
      return Task.Run(() => new ReadOffset(baseOffset, _stream.Read(_buffer, baseOffset, size)));
    }

    int readOrder = 0;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FillBuffer2(int minBytes) {
      int chunkSize = (_buffer.Length / 2);
      int chunkOffset = chunkSize * readOrder;
      ReadOffset readResult;

      _totalRead += BufferOffset;

      if (reader == null) {
        readResult = new ReadOffset(0, _stream.Read(_buffer, 0, chunkSize));
      } else {
        readResult = reader.Result;
        reader = null;
      }

      if (readResult.Read == chunkSize) {
        readOrder = (readOrder + 1) & 1;
        reader = ReadAhead(chunkSize * readOrder, chunkSize);
      }

      if (readResult.Read < minBytes) {
        throw new Exception($"Not enough bytes read from the stream. Expected minimum of {minBytes} but got {readResult.Read}");
      }

      _buffPtr = _buffBase + readResult.Offset;
      _buffLimit = _buffPtr + readResult.Read;
    }

    // IDisposable Support
    private bool disposedValue = false; // To detect redundant calls

    protected virtual void Dispose(bool disposing) {
      if (!disposedValue) {
        if (disposing) {
          _bufHandle.Free();
        }
        disposedValue = true;
      }
    }

    // This code added to correctly implement the disposable pattern.
    public void Dispose() {
      Dispose(true);
    }

  }
}
