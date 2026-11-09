using System.IO;
using Inklane.Core;

namespace Inklane.Tests;

internal static partial class Program
{
    private static void PortableStreamImport()
    {
        using var notebook = new MemoryStream();
        var sample = Sample();PortableWorkspace.Write(notebook,sample,"Example.inklane");
        var notebookBytes=notebook.ToArray();
        using var sequential=new SequentialInput(notebookBytes);
        Equal(DocumentStore.Serialize(sample),DocumentStore.Serialize(PortableWorkspace.Read(sequential).Document));
        Assert(!sequential.Closed,"Import closed the caller's stream.");
        using var trailing=new SequentialInput([..notebookBytes,1]);Throws<InvalidDataException>(()=>PortableWorkspace.Read(trailing));
        using var shortHeader=new SequentialInput(notebookBytes[..11]);Throws<EndOfStreamException>(()=>PortableWorkspace.Read(shortHeader));
        using var shortMetadata=new SequentialInput(notebookBytes[..^1]);Throws<EndOfStreamException>(()=>PortableWorkspace.Read(shortMetadata));
        var source=ImageSample();using var packed=new MemoryStream();PortableWorkspace.Write(packed,source,"image.inklane",source.SourceImagePath);
        var bytes=packed.ToArray();using var imageStream=new SequentialInput(bytes);
        var opened=PortableWorkspace.Read(imageStream);Assert(opened.SourceBytes.SequenceEqual(File.ReadAllBytes(source.SourceImagePath!)),"Sequential import changed source bytes.");
        using var offset=new MemoryStream([0,0,..bytes]);offset.Position=2;Equal(opened.SourceName,PortableWorkspace.Read(offset).SourceName);
        var corrupt=(byte[])bytes.Clone();corrupt[^1]^=1;using var changed=new SequentialInput(corrupt);Throws<InvalidDataException>(()=>PortableWorkspace.Read(changed));
        using var endless=new SequentialInput(bytes,PortableWorkspace.MaxSourceBytes+1);Throws<InvalidDataException>(()=>PortableWorkspace.Read(endless));
        Assert(endless.Consumed<=12L+System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8,4))+PortableWorkspace.MaxSourceBytes+1,"Reader consumed past its transport budget.");
        Assert(!endless.Closed,"Rejected import closed the caller's stream.");
    }

    private sealed class SequentialInput(byte[] bytes,int tail=0) : Stream
    {
        public bool Closed {get;private set;}
        public long Consumed {get;private set;}
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)
        {
            int n=(int)Math.Min(Math.Min(count,4093),bytes.LongLength+tail-Consumed);if(n<=0)return 0;
            for(int i=0;i<n;i++){buffer[offset+i]=Consumed<bytes.Length?bytes[(int)Consumed]:(byte)0;Consumed++;}return n;
        }
        public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}
    }
}
