using OpenApiDtoGenerator.Core.Resolving;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Core.Tests.Resolving;

/// <summary>
/// TDD: Шаг 1.5 — DocumentCache
///
/// Проверяем:
///   - TryGet возвращает false для отсутствующего ключа
///   - Set + TryGet возвращает сохранённый документ
///   - нормализация ключей (разные пути к одному файлу)
///   - потокобезопасность (параллельные Set/TryGet)
///   - GetOrAdd — атомарный get-or-create
/// </summary>
public class DocumentCacheTests
{
    private DocumentCache CreateSut() => new();

    // ═══════════════════════════════════════════════════════════════
    // TryGet
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TryGet_EmptyCache_ReturnsFalse()
    {
        var sut = CreateSut();

        var found = sut.TryGet("any-key", out var doc);

        found.ShouldBeFalse();
        doc.ShouldBeNull();
    }

    [Fact]
    public void TryGet_AfterSet_ReturnsTrue()
    {
        var sut = CreateSut();
        var stored = new CachedDocument("content", DocumentFormat.Yaml);

        sut.Set("key1", stored);
        var found = sut.TryGet("key1", out var doc);

        found.ShouldBeTrue();
        doc.ShouldBe(stored);
    }

    [Fact]
    public void TryGet_DifferentKey_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.Set("key1", new CachedDocument("content", DocumentFormat.Yaml));

        var found = sut.TryGet("key2", out var doc);

        found.ShouldBeFalse();
        doc.ShouldBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // Set
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Set_SameKeyTwice_SecondValueWins()
    {
        var sut = CreateSut();
        var first  = new CachedDocument("first",  DocumentFormat.Yaml);
        var second = new CachedDocument("second", DocumentFormat.Yaml);

        sut.Set("key", first);
        sut.Set("key", second);

        sut.TryGet("key", out var result);
        result.ShouldBe(second);
    }

    [Fact]
    public void Set_MultipleKeys_AllRetrievable()
    {
        var sut = CreateSut();
        var docs = Enumerable.Range(1, 5)
            .Select(i => (key: $"key{i}", doc: new CachedDocument($"content{i}", DocumentFormat.Yaml)))
            .ToList();

        foreach (var (key, doc) in docs)
            sut.Set(key, doc);

        foreach (var (key, doc) in docs)
        {
            sut.TryGet(key, out var result).ShouldBeTrue();
            result.ShouldBe(doc);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Нормализация ключей
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TryGet_KeyCaseInsensitive_ForUriKeys()
    {
        // HTTP URI — схема и хост case-insensitive
        var sut = CreateSut();
        var doc = new CachedDocument("content", DocumentFormat.Yaml);

        sut.Set("https://EXAMPLE.COM/spec.yaml", doc);

        // нижний регистр должен найти
        sut.TryGet("https://example.com/spec.yaml", out var result).ShouldBeTrue();
        result.ShouldBe(doc);
    }

    [Fact]
    public void TryGet_FilePath_CaseSensitiveOnLinux()
    {
        // На Linux пути чувствительны к регистру
        var sut = CreateSut();
        var doc = new CachedDocument("content", DocumentFormat.Yaml);

        sut.Set("/home/user/Spec.yaml", doc);

        // другой регистр — не найдёт (Linux)
        if (!OperatingSystem.IsWindows())
        {
            sut.TryGet("/home/user/spec.yaml", out var result).ShouldBeFalse();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GetOrAdd
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetOrAdd_KeyNotExists_CallsFactory()
    {
        var sut = CreateSut();
        var factoryCalled = 0;

        var result = await sut.GetOrAddAsync("key", _ =>
        {
            factoryCalled++;
            return Task.FromResult(new CachedDocument("content", DocumentFormat.Yaml));
        });

        factoryCalled.ShouldBe(1);
        result.RawContent.ShouldBe("content");
    }

    [Fact]
    public async Task GetOrAdd_KeyExists_DoesNotCallFactory()
    {
        var sut = CreateSut();
        sut.Set("key", new CachedDocument("original", DocumentFormat.Yaml));
        var factoryCalled = 0;

        var result = await sut.GetOrAddAsync("key", _ =>
        {
            factoryCalled++;
            return Task.FromResult(new CachedDocument("new", DocumentFormat.Yaml));
        });

        factoryCalled.ShouldBe(0);
        result.RawContent.ShouldBe("original");
    }

    [Fact]
    public async Task GetOrAdd_ConcurrentCallsSameKey_FactoryCalledOnce()
    {
        // Проверяем потокобезопасность: фабрика должна вызваться ровно 1 раз
        var sut = CreateSut();
        var factoryCallCount = 0;

        async Task<CachedDocument> Factory(string _)
        {
            Interlocked.Increment(ref factoryCallCount);
            await Task.Delay(20); // симулируем медленную загрузку
            return new CachedDocument("content", DocumentFormat.Yaml);
        }

        // 10 параллельных запросов к одному ключу
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => sut.GetOrAddAsync("shared-key", Factory))
            .ToArray();

        await Task.WhenAll(tasks);

        // Все получили одинаковый результат
        foreach (var t in tasks)
            (await t).RawContent.ShouldBe("content");

        // Фабрика вызвана не более одного раза (допускаем 1 из-за race window)
        factoryCallCount.ShouldBeLessThanOrEqualTo(1);
    }

    // ═══════════════════════════════════════════════════════════════
    // Count / Clear
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Count_EmptyCache_IsZero()
    {
        var sut = CreateSut();
        sut.Count.ShouldBe(0);
    }

    [Fact]
    public void Count_AfterTwoSets_IsTwo()
    {
        var sut = CreateSut();
        sut.Set("k1", new CachedDocument("c1", DocumentFormat.Yaml));
        sut.Set("k2", new CachedDocument("c2", DocumentFormat.Json));

        sut.Count.ShouldBe(2);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var sut = CreateSut();
        sut.Set("k1", new CachedDocument("c1", DocumentFormat.Yaml));
        sut.Set("k2", new CachedDocument("c2", DocumentFormat.Yaml));

        sut.Clear();

        sut.Count.ShouldBe(0);
        sut.TryGet("k1", out _).ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // Guard
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TryGet_NullKey_ThrowsArgumentException()
    {
        var sut = CreateSut();
        Should.Throw<ArgumentException>(() => sut.TryGet(null!, out _));
    }

    [Fact]
    public void Set_NullKey_ThrowsArgumentException()
    {
        var sut = CreateSut();
        Should.Throw<ArgumentException>(
            () => sut.Set(null!, new CachedDocument("c", DocumentFormat.Yaml)));
    }

    [Fact]
    public void Set_NullDocument_ThrowsArgumentNullException()
    {
        var sut = CreateSut();
        Should.Throw<ArgumentNullException>(() => sut.Set("key", null!));
    }
}
