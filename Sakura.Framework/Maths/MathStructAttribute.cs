// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Maths;

/// <summary>
/// The attribute that marks a struct as a math-related struct and
/// enables source generation for implicit conversions to and from <see cref="System.Numerics"/> types.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public class MathStructAttribute : Attribute
{

}
