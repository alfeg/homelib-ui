namespace MyHomeLib.Torrent;

/// <summary>
/// A seekable read-only <see cref="Stream"/> backed by HTTP Range requests.
/// Each <see cref="Read"/> call issues a GET request with a Range header covering
/// exactly the requested bytes. TorrServe (and any HTTP/1.1 server supporting
/// Accept-Ranges) will download only the torrent pieces covering that range.
/// </summary>
public sealed class HttpRangeStream(HttpClient http, string url, long hintLength) : Stream
{
    private long _position;
    private long _length = hintLength;
    private bool _lengthResolved;

    public override bool CanRead  => true;
    public override bool CanSeek  => true;
    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            if (!_lengthResolved)
            {
                // Resolve actual length synchronously on first access
                _length = ResolveLength().GetAwaiter().GetResult();
                _lengthResolved = true;
            }
            return _length;
        }
    }

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    private async Task<long> ResolveLength()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await http.SendAsync(req);
            if (resp.Content.Headers.ContentLength is > 0)
                return resp.Content.Headers.ContentLength.Value;
        }
        catch { /* fall back to hint */ }
        return _length;
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
    {
        if (_position >= Length) return 0;
        var end = Math.Min(_position + count - 1, Length - 1);
        if (end < _position) return 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(_position, end);

        using var resp = await http.SendAsync(req,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        int totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
            if (read == 0) break;
            totalRead += read;
        }

        _position += totalRead;
        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin   => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End     => Length + offset,
            _                  => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
}
