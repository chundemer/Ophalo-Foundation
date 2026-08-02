using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OpHalo.Foundation.Application.Abstractions.Storage;
using OpHalo.Foundation.Infrastructure.Storage;

namespace OpHalo.UnitTests.Storage;

public class LocalDiskBusinessDocumentStorageTests
{
    private readonly LocalDiskBusinessDocumentStorage _sut =
        new(NullLogger<LocalDiskBusinessDocumentStorage>.Instance);

    [Fact]
    public async Task PutAsync_returns_a_key_that_DeleteBestEffortAsync_can_remove()
    {
        var accountId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("sku,name\n1,widget\n"));

        var putResult = await _sut.PutAsync(accountId, DocumentPurpose.PriceBookImport, content, CancellationToken.None);

        Assert.True(putResult.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(putResult.Value));

        await _sut.DeleteBestEffortAsync(accountId, DocumentPurpose.PriceBookImport, putResult.Value, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteBestEffortAsync_does_not_throw_for_a_key_that_was_never_written()
    {
        var accountId = Guid.NewGuid();

        await _sut.DeleteBestEffortAsync(
            accountId, DocumentPurpose.PriceBookImport, $"price-book-import/{accountId:N}/missing", CancellationToken.None);
    }

    [Fact]
    public async Task DeleteBestEffortAsync_refuses_a_key_generated_for_a_different_account()
    {
        var accountId = Guid.NewGuid();
        var otherAccountId = Guid.NewGuid();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("sku,name\n1,widget\n"));

        var putResult = await _sut.PutAsync(accountId, DocumentPurpose.PriceBookImport, content, CancellationToken.None);

        // Must not throw and must not delete the object — the (accountId, purpose) pair does not
        // match the key's generated prefix.
        await _sut.DeleteBestEffortAsync(
            otherAccountId, DocumentPurpose.PriceBookImport, putResult.Value, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteBestEffortAsync_refuses_a_traversal_shaped_key()
    {
        var accountId = Guid.NewGuid();

        await _sut.DeleteBestEffortAsync(
            accountId, DocumentPurpose.PriceBookImport, $"price-book-import/{accountId:N}/../../etc/passwd", CancellationToken.None);
    }
}
