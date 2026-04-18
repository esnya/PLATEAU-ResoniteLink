using System.Diagnostics.CodeAnalysis;
using System.Text;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Profiles;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class LocalCityGmlSourceFileParserStreamingTests
{
    [Fact]
    public async Task SourceFilePipeline_StreamParsedCityObjectsAsync_YieldsFirstObjectBeforeBlockedSecondHalfOfStream()
    {
        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 139.0100 10</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-1">
                  <gml:name>Building One</gml:name>
                  <bldg:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-1">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-1">
                              <gml:posList>35.0000 139.0000 0 35.0000 139.0010 0 35.0010 139.0010 8 35.0000 139.0000 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod1MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-2">
                  <gml:name>Building Two</gml:name>
                  <bldg:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-2">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-2">
                              <gml:posList>35.0020 139.0020 0 35.0020 139.0030 0 35.0030 139.0030 8 35.0020 139.0020 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod1MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;
        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        int firstMemberOffset = xml.IndexOf("<core:cityObjectMember>", StringComparison.Ordinal);
        int secondMemberOffset = xml.IndexOf("<core:cityObjectMember>", firstMemberOffset + 1, StringComparison.Ordinal);
        GateableDatasetContentSource datasetSource = new(bytes, secondMemberOffset);
        SourceFileDescriptor sourceFile = new(
            "udx/bldg/53394525/streaming.gml",
            "bldg",
            "53394525",
            RequiresMeshAreaFilter: false);

        SourceFilePipeline[] pipelines = await LocalCityGmlResonitePlanBuilder.CreateSourceFilePipelinesCoreAsync(
            [sourceFile],
            datasetSource,
            [],
            progressReporter: null,
            new LodFilteringStrategy(),
            CancellationToken.None);

        await using IAsyncEnumerator<BootstrapParsedCityObject> enumerator =
            pipelines.Single().StreamParsedCityObjectsAsync().GetAsyncEnumerator();

        Task<bool> firstMoveTask = enumerator.MoveNextAsync().AsTask();
        Assert.Same(firstMoveTask, await Task.WhenAny(firstMoveTask, Task.Delay(TimeSpan.FromSeconds(1))));
        Assert.True(await firstMoveTask);
        Assert.Equal("Building One", enumerator.Current.DisplayName);

        Task<bool> secondMoveTask = enumerator.MoveNextAsync().AsTask();
        Assert.False(secondMoveTask.IsCompleted);

        datasetSource.Release();

        Assert.True(await secondMoveTask);
        Assert.Equal("Building Two", enumerator.Current.DisplayName);
    }

    private sealed class GateableDatasetContentSource(byte[] payload, int gateOffset) : IPlateauDatasetContentSource
    {
        private readonly TaskCompletionSource releaseSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string SourcePath => "/tmp/streaming";

        public IReadOnlyList<string> EnumerateFiles()
        {
            return ["udx/bldg/53394525/streaming.gml"];
        }

        public bool FileExists(string relativePath)
        {
            return true;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership is transferred to the caller as a Stream result.")]
        public ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            Stream stream = new GateableReadStream(payload, gateOffset, releaseSignal.Task);
            return ValueTask.FromResult(stream);
        }

        public Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Release()
        {
            releaseSignal.TrySetResult();
        }
    }

    private sealed class GateableReadStream(
        byte[] payload,
        int gateOffset,
        Task releaseTask)
        : Stream
    {
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => payload.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (position >= payload.Length)
            {
                return 0;
            }

            if (position >= gateOffset && !releaseTask.IsCompleted)
            {
                await releaseTask.WaitAsync(cancellationToken);
            }

            int availableCount = payload.Length - position;
            if (position < gateOffset && !releaseTask.IsCompleted)
            {
                availableCount = Math.Min(availableCount, gateOffset - position);
            }

            int bytesToCopy = Math.Min(buffer.Length, availableCount);
            payload.AsMemory(position, bytesToCopy).CopyTo(buffer);
            position += bytesToCopy;
            return bytesToCopy;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
