using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using SIPS.PostgreSQL.Gateway;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Enums;
using Xunit;
using FluentAssertions;

namespace SIPS.Core.Tests.Tests;

public class IncomingRecorderTests
{
    private readonly Mock<IStorageBroker> _mockStorage;
    private readonly Mock<ILogger<IncomingRecorder>> _mockLogger;
    private IncomingRecorder _recorder;
    private readonly Mock<DbSet<ISOMessage>> _mockDbSet;

    public IncomingRecorderTests()
    {
        _mockStorage = new Mock<IStorageBroker>();
        _mockLogger = new Mock<ILogger<IncomingRecorder>>();
        _mockDbSet = new Mock<DbSet<ISOMessage>>();
    }

    private void InitializeRecorder()
    {
        _mockStorage.Setup(x => x.ISOMessages).Returns(_mockDbSet.Object);
        _recorder = new IncomingRecorder(_mockLogger.Object, _mockStorage.Object);
    }

    [Fact]
    public async Task TryRecordIncomingTransactionAsync_Success_ReturnsNew()
    {
        // Arrange
        InitializeRecorder();
        var entity = new ISOMessage { TxId = "TX1", MsgId = "M1", MessageType = ISOMessageType.TransactionRequest };
        _mockStorage.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        var result = await _recorder.TryRecordIncomingTransactionAsync(entity, CancellationToken.None);

        // Assert
        result.Message.Should().Be(entity);
        result.IsNew.Should().BeTrue();
    }

    [Fact]
    public async Task TryRecordIncomingTransactionAsync_TxIdConstraint_ReturnsExisting_IsNewFalse()
    {
        // Arrange
        var entity = new ISOMessage { TxId = "TX1", MsgId = "M1", MessageType = ISOMessageType.TransactionRequest };
        var existing = new ISOMessage { Id = 10, TxId = "TX1", MsgId = "M1_OLD", MessageType = ISOMessageType.TransactionRequest };

        SetupDbSetToThrowOnSave(CreatePostgresException("23505", "ux_iso_msg_type_txid"));
        SetupDbSetQuery(new List<ISOMessage> { existing });
        InitializeRecorder();

        // Act
        var result = await _recorder.TryRecordIncomingTransactionAsync(entity, CancellationToken.None);

        // Assert
        result.Message.Should().NotBeNull();
        result.Message!.Id.Should().Be(10);
        result.IsNew.Should().BeFalse();
        // Should have searched by TxId
        _mockLogger.Invocations.Any(i => i.Arguments.Any(a => a?.ToString()?.Contains("Structural De-duplication trigger (TxId)") == true)).Should().BeTrue();
    }

    [Fact]
    public async Task TryRecordIncomingTransactionAsync_MsgIdConstraint_ReturnsExisting_IsNewFalse()
    {
        // Arrange
        var entity = new ISOMessage { TxId = "TX1", MsgId = "M1", MessageType = ISOMessageType.TransactionRequest };
        var existing = new ISOMessage { Id = 20, TxId = "TX_OLD", MsgId = "M1", MessageType = ISOMessageType.TransactionRequest };

        SetupDbSetToThrowOnSave(CreatePostgresException("23505", "ux_iso_msg_type_msgid"));
        SetupDbSetQuery(new List<ISOMessage> { existing });
        InitializeRecorder();

        // Act
        var result = await _recorder.TryRecordIncomingTransactionAsync(entity, CancellationToken.None);

        // Assert
        result.Message.Should().NotBeNull();
        result.Message!.Id.Should().Be(20);
        result.IsNew.Should().BeFalse();
        // Should have searched by MsgId
        _mockLogger.Invocations.Any(i => i.Arguments.Any(a => a?.ToString()?.Contains("Structural De-duplication trigger (MsgId)") == true)).Should().BeTrue();
    }

    private void SetupDbSetToThrowOnSave(PostgresException pgEx)
    {
        var dbEx = new DbUpdateException("Duplicate", pgEx);
        _mockStorage.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(dbEx);
    }

    private void SetupDbSetQuery(IEnumerable<ISOMessage> data)
    {
        var queryable = data.AsQueryable();
        
        // Mock IQueryable for async operation
        _mockDbSet.As<IQueryable<ISOMessage>>().Setup(m => m.Provider).Returns(new TestAsyncQueryProvider<ISOMessage>(queryable.Provider));
        _mockDbSet.As<IQueryable<ISOMessage>>().Setup(m => m.Expression).Returns(queryable.Expression);
        _mockDbSet.As<IQueryable<ISOMessage>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        _mockDbSet.As<IQueryable<ISOMessage>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        
        // Mock IAsyncEnumerable for EF Core async methods
        _mockDbSet.As<IAsyncEnumerable<ISOMessage>>().Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<ISOMessage>(queryable.GetEnumerator()));
    }

    private static PostgresException CreatePostgresException(string sqlState, string constraintName)
    {
        var ex = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PostgresException)) as PostgresException;
        
        // Set SqlState (Backing field usually <SqlState>k__BackingField or _sqlState)
        var sqlStateField = typeof(PostgresException).GetField("<SqlState>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        if (sqlStateField == null) sqlStateField = typeof(PostgresException).GetField("_sqlState", BindingFlags.Instance | BindingFlags.NonPublic);
        sqlStateField?.SetValue(ex, sqlState);
        
        // Set ConstraintName
        var constraintNameField = typeof(PostgresException).GetField("<ConstraintName>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        if (constraintNameField == null) constraintNameField = typeof(PostgresException).GetField("_constraintName", BindingFlags.Instance | BindingFlags.NonPublic);
        constraintNameField?.SetValue(ex, constraintName);
        
        return ex!;
    }
}

// Boilerplate for mocking Async Queryable
internal class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
{
    private readonly IQueryProvider _inner;

    internal TestAsyncQueryProvider(IQueryProvider inner)
    {
        _inner = inner;
    }

    public IQueryable CreateQuery(Expression expression)
    {
        return new TestAsyncEnumerable<TEntity>(expression);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return new TestAsyncEnumerable<TElement>(expression);
    }

    public object Execute(Expression expression)
    {
        return _inner.Execute(expression);
    }

    public TResult Execute<TResult>(Expression expression)
    {
        return _inner.Execute<TResult>(expression);
    }

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
    {
        // Execute sync as mock
        var resultType = typeof(TResult).GetGenericArguments()[0];
        var executionResult = typeof(IQueryProvider)
                                 .GetMethod(
                                     name: nameof(IQueryProvider.Execute),
                                     genericParameterCount: 1,
                                     types: new[] { typeof(Expression) })
                                 .MakeGenericMethod(resultType)
                                 .Invoke(this, new[] { expression });

        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))
            .MakeGenericMethod(resultType)
            .Invoke(null, new[] { executionResult });
    }
}

internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
    public TestAsyncEnumerable(Expression expression) : base(expression) { }
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }
    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
}

internal class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    private readonly IEnumerator<T> _inner;
    public TestAsyncEnumerator(IEnumerator<T> inner)
    {
        _inner = inner;
    }
    public ValueTask DisposeAsync()
    {
        _inner.Dispose();
        return ValueTask.CompletedTask;
    }
    public ValueTask<bool> MoveNextAsync()
    {
        return ValueTask.FromResult(_inner.MoveNext());
    }
    public T Current => _inner.Current;
}
