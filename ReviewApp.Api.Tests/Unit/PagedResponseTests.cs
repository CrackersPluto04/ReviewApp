using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Tests.Unit;

public class PagedResponseTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 20, 2)]
    public void TotalPages_RoundsUp(int totalCount, int pageSize, int expected)
    {
        new PagedResponse<int>([], totalCount, 1, pageSize).TotalPages.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    public void TotalPages_WithoutAValidPageSize_IsZero(int totalCount, int pageSize)
    {
        // search/all builds an empty response with page size 0; 0 / 0.0 is NaN and (int)NaN is undefined
        new PagedResponse<int>([], totalCount, 1, pageSize).TotalPages.ShouldBe(0);
    }
}
