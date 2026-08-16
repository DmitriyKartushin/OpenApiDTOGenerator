using OpenApiDtoGenerator.Core.Resolving;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Core.Tests.Resolving;

/// <summary>
/// TDD: Шаг 1.7 — CycleDetector
///
/// Сценарии:
///   - обычный вход/выход без цикла
///   - прямой цикл: A → A
///   - косвенный цикл: A → B → C → A
///   - независимые ветки не мешают друг другу
///   - повторное использование после сброса
///   - IsVisiting корректно отражает состояние стека
///   - VisitingStack возвращает снимок текущего пути
///   - потокобезопасность: каждый поток имеет свой контекст
/// </summary>
public class CycleDetectorTests
{
    private CycleDetector CreateSut() => new();

    // ═══════════════════════════════════════════════════════════════
    // HAPPY PATH — нет циклов
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enter_NewKey_DoesNotThrow()
    {
        var sut = CreateSut();

        Should.NotThrow(() => sut.Enter("A"));
    }

    [Fact]
    public void Enter_ThenLeave_KeyRemovedFromStack()
    {
        var sut = CreateSut();

        sut.Enter("A");
        sut.Leave("A");

        sut.IsVisiting("A").ShouldBeFalse();
    }

    [Fact]
    public void Enter_SequentialDifferentKeys_NoThrow()
    {
        // A → выход → B → выход — линейная цепочка, не цикл
        var sut = CreateSut();

        sut.Enter("A");
        sut.Leave("A");
        sut.Enter("B");
        sut.Leave("B");

        sut.IsVisiting("A").ShouldBeFalse();
        sut.IsVisiting("B").ShouldBeFalse();
    }

    [Fact]
    public void Enter_NestedDifferentKeys_NoThrow()
    {
        // A → B → C → выход C → выход B → выход A
        var sut = CreateSut();

        sut.Enter("A");
        sut.Enter("B");
        sut.Enter("C");
        sut.Leave("C");
        sut.Leave("B");
        sut.Leave("A");

        sut.IsVisiting("A").ShouldBeFalse();
        sut.IsVisiting("B").ShouldBeFalse();
        sut.IsVisiting("C").ShouldBeFalse();
    }

    [Fact]
    public void Enter_SameKeyAfterFullLeave_DoesNotThrow()
    {
        // A → выход → A снова (не цикл — первый визит завершён)
        var sut = CreateSut();

        sut.Enter("A");
        sut.Leave("A");

        Should.NotThrow(() => sut.Enter("A"));
        sut.Leave("A");
    }

    // ═══════════════════════════════════════════════════════════════
    // DIRECT CYCLE — A → A
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enter_SameKeyTwiceWithoutLeave_ThrowsCyclicRefException()
    {
        var sut = CreateSut();

        sut.Enter("A");

        Should.Throw<CyclicReferenceException>(() => sut.Enter("A"));
    }

    [Fact]
    public void Enter_DirectCycle_ExceptionContainsKey()
    {
        var sut = CreateSut();
        sut.Enter("#/components/schemas/TreeNode");

        var ex = Should.Throw<CyclicReferenceException>(
            () => sut.Enter("#/components/schemas/TreeNode"));

        ex.CyclicKey.ShouldBe("#/components/schemas/TreeNode");
    }

    [Fact]
    public void Enter_DirectCycle_ExceptionContainsCyclePath()
    {
        var sut = CreateSut();
        sut.Enter("A");
        sut.Enter("B");

        var ex = Should.Throw<CyclicReferenceException>(() => sut.Enter("A"));

        // Путь должен отражать стек: A → B → A
        ex.CyclePath.ShouldContain("A");
        ex.CyclePath.ShouldContain("B");
    }

    // ═══════════════════════════════════════════════════════════════
    // INDIRECT CYCLE — A → B → C → A
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enter_IndirectCycle_ThrowsCyclicRefException()
    {
        var sut = CreateSut();

        sut.Enter("A");
        sut.Enter("B");
        sut.Enter("C");

        Should.Throw<CyclicReferenceException>(() => sut.Enter("A"));
    }

    [Fact]
    public void Enter_IndirectCycle_ExceptionContainsFullPath()
    {
        var sut = CreateSut();
        sut.Enter("SchemaA");
        sut.Enter("SchemaB");
        sut.Enter("SchemaC");

        var ex = Should.Throw<CyclicReferenceException>(() => sut.Enter("SchemaA"));

        ex.CyclePath.ShouldContain("SchemaA");
        ex.CyclePath.ShouldContain("SchemaB");
        ex.CyclePath.ShouldContain("SchemaC");
    }

    [Fact]
    public void Enter_IndirectCycle_CyclicKeyIsCorrect()
    {
        var sut = CreateSut();
        sut.Enter("A");
        sut.Enter("B");
        sut.Enter("C");

        var ex = Should.Throw<CyclicReferenceException>(() => sut.Enter("A"));

        ex.CyclicKey.ShouldBe("A");
    }

    // ═══════════════════════════════════════════════════════════════
    // INDEPENDENT BRANCHES — параллельные ветки не мешают
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enter_BranchBacktrackThenNewBranch_NoThrow()
    {
        // Дерево: Root → A → B → (выход) → C → B (B уже был в другой ветке, но сейчас не в стеке)
        var sut = CreateSut();

        sut.Enter("Root");
          sut.Enter("A");
            sut.Enter("B");
            sut.Leave("B");
          sut.Leave("A");
          sut.Enter("C");
            Should.NotThrow(() => sut.Enter("B")); // B не в стеке — не цикл
            sut.Leave("B");
          sut.Leave("C");
        sut.Leave("Root");
    }

    [Fact]
    public void Enter_DiamondShape_NoThrow()
    {
        // A → B → D
        // A → C → D   (D посещается дважды, но не через себя)
        var sut = CreateSut();

        sut.Enter("A");
          sut.Enter("B");
            sut.Enter("D");
            sut.Leave("D");
          sut.Leave("B");
          sut.Enter("C");
            Should.NotThrow(() => sut.Enter("D")); // не цикл
            sut.Leave("D");
          sut.Leave("C");
        sut.Leave("A");
    }

    // ═══════════════════════════════════════════════════════════════
    // IsVisiting
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void IsVisiting_BeforeEnter_False()
    {
        var sut = CreateSut();
        sut.IsVisiting("A").ShouldBeFalse();
    }

    [Fact]
    public void IsVisiting_AfterEnter_True()
    {
        var sut = CreateSut();
        sut.Enter("A");
        sut.IsVisiting("A").ShouldBeTrue();
    }

    [Fact]
    public void IsVisiting_AfterLeave_False()
    {
        var sut = CreateSut();
        sut.Enter("A");
        sut.Leave("A");
        sut.IsVisiting("A").ShouldBeFalse();
    }

    [Fact]
    public void IsVisiting_IntermediateNode_TrueWhileNested()
    {
        var sut = CreateSut();
        sut.Enter("A");
        sut.Enter("B");

        sut.IsVisiting("A").ShouldBeTrue(); // A всё ещё в стеке
        sut.IsVisiting("B").ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // VisitingStack — снимок текущего пути
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void VisitingStack_Empty_ReturnsEmptyCollection()
    {
        var sut = CreateSut();
        sut.VisitingStack.ShouldBeEmpty();
    }

    [Fact]
    public void VisitingStack_AfterEnters_ReturnsCorrectOrder()
    {
        var sut = CreateSut();
        sut.Enter("A");
        sut.Enter("B");
        sut.Enter("C");

        var stack = sut.VisitingStack.ToList();

        // Порядок: первый вошедший первый в списке (FIFO / путь от корня)
        stack[0].ShouldBe("A");
        stack[1].ShouldBe("B");
        stack[2].ShouldBe("C");
    }

    [Fact]
    public void VisitingStack_AfterLeave_UpdatesCorrectly()
    {
        var sut = CreateSut();
        sut.Enter("A");
        sut.Enter("B");
        sut.Leave("B");

        var stack = sut.VisitingStack.ToList();

        stack.Count.ShouldBe(1);
        stack[0].ShouldBe("A");
    }

    [Fact]
    public void VisitingStack_IsSnapshot_NotLiveReference()
    {
        var sut = CreateSut();
        sut.Enter("A");

        var snapshot = sut.VisitingStack.ToList();
        sut.Enter("B"); // изменяем состояние после снимка

        // снимок не должен измениться
        snapshot.Count.ShouldBe(1);
    }

    // ═══════════════════════════════════════════════════════════════
    // Reset
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Reset_ClearsStack()
    {
        var sut = CreateSut();
        sut.Enter("A");
        sut.Enter("B");

        sut.Reset();

        sut.VisitingStack.ShouldBeEmpty();
        sut.IsVisiting("A").ShouldBeFalse();
        sut.IsVisiting("B").ShouldBeFalse();
    }

    [Fact]
    public void Reset_AllowsReenteringPreviousKeys()
    {
        var sut = CreateSut();
        sut.Enter("A");
        sut.Reset();

        Should.NotThrow(() => sut.Enter("A"));
    }

    // ═══════════════════════════════════════════════════════════════
    // GUARD
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enter_NullKey_ThrowsArgumentException()
    {
        var sut = CreateSut();
        Should.Throw<ArgumentException>(() => sut.Enter(null!));
    }

    [Fact]
    public void Enter_EmptyKey_ThrowsArgumentException()
    {
        var sut = CreateSut();
        Should.Throw<ArgumentException>(() => sut.Enter(string.Empty));
    }

    [Fact]
    public void Leave_KeyNotInStack_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        Should.Throw<InvalidOperationException>(() => sut.Leave("notInStack"));
    }

    [Fact]
    public void Leave_WrongOrderKey_ThrowsInvalidOperationException()
    {
        // Вошли A → B, но пытаемся выйти из A (не из верхушки стека)
        var sut = CreateSut();
        sut.Enter("A");
        sut.Enter("B");

        Should.Throw<InvalidOperationException>(() => sut.Leave("A"));
    }

    // ═══════════════════════════════════════════════════════════════
    // THREAD SAFETY — каждый поток независим
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Enter_ConcurrentThreads_EachHasOwnContext()
    {
        // Каждый поток использует свой CycleDetector — независимость гарантирована
        // самой архитектурой (один detector = один контекст резолвинга).
        // Тест проверяет что параллельное использование разных экземпляров
        // не вызывает гонок данных.
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            try
            {
                var sut = CreateSut();
                sut.Enter("A");
                sut.Enter("B");
                sut.Enter("C");
                sut.Leave("C");
                sut.Leave("B");
                sut.Leave("A");
                sut.VisitingStack.ShouldBeEmpty();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        errors.ShouldBeEmpty();
    }
}
