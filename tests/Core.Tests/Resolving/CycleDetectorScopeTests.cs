using OpenApiDtoGenerator.Core.Resolving;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Core.Tests.Resolving;

/// <summary>
/// TDD: CycleDetectorScope — RAII-обёртка
/// </summary>
public class CycleDetectorScopeTests
{
    // ═══════════════════════════════════════════════════════════════
    // HAPPY PATH
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Scope_AfterUsing_KeyRemovedFromStack()
    {
        var detector = new CycleDetector();

        using (detector.Scope("A"))
        {
            detector.IsVisiting("A").ShouldBeTrue();
        }

        detector.IsVisiting("A").ShouldBeFalse();
    }

    [Fact]
    public void Scope_Nested_CorrectStackAtEachLevel()
    {
        var detector = new CycleDetector();

        using (detector.Scope("A"))
        {
            detector.IsVisiting("A").ShouldBeTrue();

            using (detector.Scope("B"))
            {
                detector.IsVisiting("A").ShouldBeTrue();
                detector.IsVisiting("B").ShouldBeTrue();

                using (detector.Scope("C"))
                {
                    detector.VisitingStack.Count.ShouldBe(3);
                }

                // C вышел
                detector.IsVisiting("C").ShouldBeFalse();
                detector.IsVisiting("B").ShouldBeTrue();
            }

            // B вышел
            detector.IsVisiting("B").ShouldBeFalse();
            detector.IsVisiting("A").ShouldBeTrue();
        }

        // A вышел
        detector.VisitingStack.ShouldBeEmpty();
    }

    [Fact]
    public void Scope_ExceptionInsideBlock_LeavesCalledCorrectly()
    {
        var detector = new CycleDetector();

        try
        {
            using (detector.Scope("A"))
            using (detector.Scope("B"))
            {
                throw new InvalidOperationException("simulated error");
            }
        }
        catch (InvalidOperationException) { /* ожидаем */ }

        // Несмотря на исключение — стек должен быть пуст
        detector.VisitingStack.ShouldBeEmpty();
        detector.IsVisiting("A").ShouldBeFalse();
        detector.IsVisiting("B").ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // CYCLE via Scope
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Scope_CycleDetected_ThrowsCyclicReferenceException()
    {
        var detector = new CycleDetector();

        using (detector.Scope("A"))
        using (detector.Scope("B"))
        {
            Should.Throw<CyclicReferenceException>(() =>
            {
                using var _ = detector.Scope("A"); // цикл!
            });
        }
    }

    [Fact]
    public void Scope_AfterCycleException_StackRemainsConsistent()
    {
        var detector = new CycleDetector();

        using (detector.Scope("A"))
        {
            try
            {
                using var _ = detector.Scope("A"); // цикл — бросает до Enter
            }
            catch (CyclicReferenceException) { /* ожидаем */ }

            // "A" всё ещё в стеке (внешний scope не завершён)
            detector.IsVisiting("A").ShouldBeTrue();
            detector.VisitingStack.Count.ShouldBe(1);
        }

        // Теперь внешний scope вышел
        detector.VisitingStack.ShouldBeEmpty();
    }
}
