// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Tests.Graphics.Textures;

/// <summary>
/// Test for <see cref="TextureUploadQueue"/>
/// </summary>
[TestFixture]
public class TextureUploadQueueTest
{
    private static readonly int[] expected = new[] { 0, 1 };
    private static readonly int[] expected_array = new[] { 0, 1, 2, 3 };

    [Test]
    public void StopsAtBudgetAndCarriesRemainderOver()
    {
        var queue = new TextureUploadQueue
        {
            BytesPerFrameBudget = 100
        };
        var order = new List<int>();

        for (int i = 0; i < 4; i++)
        {
            int id = i;
            queue.Enqueue(() => order.Add(id), 60); // two per frame (60 + 60 >= 100)
        }

        queue.Process();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(order, Is.EqualTo(expected));
            Assert.That(queue.PendingCount, Is.EqualTo(2));
        }

        queue.Process();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(order, Is.EqualTo(expected_array));
            Assert.That(queue.PendingCount, Is.Zero);
        }
    }

    [Test]
    public void AlwaysProcessesAtLeastOneEvenIfOverBudget()
    {
        var queue = new TextureUploadQueue
        {
            BytesPerFrameBudget = 10
        };
        int ran = 0;

        queue.Enqueue(() => ran++, 1_000_000); // single item far exceeds the budget
        queue.Process();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ran, Is.EqualTo(1));
            Assert.That(queue.PendingCount, Is.Zero);
        }
    }

    [Test]
    public void ProcessOnEmptyQueueIsNoOp()
    {
        var queue = new TextureUploadQueue();
        Assert.DoesNotThrow(queue.Process);
    }
}
