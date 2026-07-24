using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.TestSupport
{
    /// <summary>
    /// Shared helpers for mocking <see cref="IMongoCollection{T}"/>. The repositories under test issue
    /// their reads through the fluent <c>Find(...)</c>/<c>FindAsync(...)</c> paths, both of which the driver
    /// funnels into <see cref="IMongoCollection{T}.FindAsync"/>, and their writes through the
    /// Insert/Replace/Update/Delete/BulkWrite members. This helper wires all of those to in-memory data so
    /// the repository logic (filters, projections, result mapping) can be exercised without a live MongoDB.
    /// </summary>
    public static class MongoMock
    {
        /// <summary>Build a collection mock that returns <paramref name="items"/> for reads and acknowledges writes.</summary>
        public static Mock<IMongoCollection<T>> Collection<T>(IEnumerable<T>? items = null)
        {
            var list = (items ?? Enumerable.Empty<T>()).ToList();
            var col = new Mock<IMongoCollection<T>>();
            SetupFind(col, list);
            SetupCount(col, list.Count);
            SetupWrites(col);
            return col;
        }

        /// <summary>A fresh cursor over <paramref name="items"/>. A new one is returned per read so repeated reads work.</summary>
        public static IAsyncCursor<T> Cursor<T>(IEnumerable<T> items)
        {
            var list = items.ToList();
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(list);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true).ReturnsAsync(false);
            return cursor.Object;
        }

        public static void SetupFind<T>(Mock<IMongoCollection<T>> col, IEnumerable<T> items)
        {
            var list = items.ToList();
            col.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<T>>(),
                    It.IsAny<FindOptions<T, T>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Cursor(list));
        }

        /// <summary>Wire the projected read overload (used by <c>Find(...).Project(...)</c>).</summary>
        public static void SetupProjectedFind<T, TProjection>(Mock<IMongoCollection<T>> col, IEnumerable<TProjection> projected)
        {
            var list = projected.ToList();
            col.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<T>>(),
                    It.IsAny<FindOptions<T, TProjection>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Cursor(list));
        }

        public static void SetupCount<T>(Mock<IMongoCollection<T>> col, long count)
        {
            col.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<T>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(count);
        }

        public static void SetupWrites<T>(Mock<IMongoCollection<T>> col)
        {
            col.Setup(c => c.InsertOneAsync(It.IsAny<T>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            col.Setup(c => c.InsertManyAsync(It.IsAny<IEnumerable<T>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            col.Setup(c => c.ReplaceOneAsync(
                    It.IsAny<FilterDefinition<T>>(), It.IsAny<T>(), It.IsAny<ReplaceOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReplaceOneResult.Acknowledged(1, 1, null));

            col.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<T>>(), It.IsAny<UpdateDefinition<T>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            col.Setup(c => c.UpdateManyAsync(
                    It.IsAny<FilterDefinition<T>>(), It.IsAny<UpdateDefinition<T>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(2, 2, null));

            col.Setup(c => c.DeleteOneAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteResult.Acknowledged(1));
            col.Setup(c => c.DeleteManyAsync(It.IsAny<FilterDefinition<T>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteResult.Acknowledged(2));

            col.Setup(c => c.BulkWriteAsync(
                    It.IsAny<IEnumerable<WriteModel<T>>>(), It.IsAny<BulkWriteOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<WriteModel<T>> reqs, BulkWriteOptions _, CancellationToken _) =>
                    new BulkWriteResult<T>.Acknowledged(
                        reqs.Count(), reqs.Count(), 0, 0, reqs.Count(),
                        reqs.ToList(), new List<BulkWriteUpsert>()));
        }
    }
}
