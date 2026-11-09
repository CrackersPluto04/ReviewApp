using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests.Unit;

// CollectionService.MergeOrder: an order made on another device applied to the current items
public class CollectionOrderTests
{
    [Theory]
    // Plain reorder
    [InlineData(new[] { 1, 2, 3 }, new[] { 3, 1, 2 }, new[] { 3, 1, 2 })]
    // Items added elsewhere meanwhile go after the requested ones, in their current order
    [InlineData(new[] { 1, 2, 3, 4 }, new[] { 2, 1 }, new[] { 2, 1, 3, 4 })]
    // Items removed elsewhere meanwhile are ignored
    [InlineData(new[] { 1, 3 }, new[] { 3, 2, 1 }, new[] { 3, 1 })]
    // Duplicates in the request count once
    [InlineData(new[] { 1, 2 }, new[] { 2, 2, 1 }, new[] { 2, 1 })]
    // Nothing requested keeps the current order
    [InlineData(new[] { 1, 2 }, new int[0], new[] { 1, 2 })]
    // Empty collection
    [InlineData(new int[0], new[] { 1 }, new int[0])]
    public void MergeOrder(int[] current, int[] requested, int[] expected)
    {
        CollectionService.MergeOrder(current, requested).ShouldBe(expected);
    }
}
