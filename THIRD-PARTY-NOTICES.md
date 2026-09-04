# Third-party notices

Tedd.FastNoise incorporates third-party material under the licences reproduced below.

---

## FastNoiseLite

Tedd.FastNoise is a port of, and is built on, **FastNoiseLite 1.1.1** by Jordan Peck.

Upstream: <https://github.com/Auburn/FastNoiseLite>

### What is used, and where

| Location | Relationship to upstream |
| --- | --- |
| `src/Tedd.FastNoise/Internal/FastNoiseLiteCore.cs` | Verbatim copy of `CSharp/FastNoiseLite.cs`. Changed only in namespace and accessibility; every algorithm is byte-for-byte the original. Used at runtime for OpenSimplex2S and for all domain warping. |
| `src/Tedd.FastNoise/Internal/Tables.cs` | Verbatim copy of the four gradient and feature-point lookup tables. |
| `src/Tedd.FastNoise/Internal/Hashing.cs` | Port of the hash, value and gradient functions. Same constants, shifts and operation order; rewritten to be generic over lane width. |
| `src/Tedd.FastNoise/Internal/NoiseMath.cs` | Port of the rounding and interpolation helpers. |
| `src/Tedd.FastNoise/Internal/Kernels/*.cs` | Ports of the OpenSimplex2, Perlin, Value, ValueCubic and Cellular algorithms, of the coordinate transforms, and of the fractal octave loops. Restructured for vectorisation; numerically identical. |
| `src/Tedd.FastNoise.Tests/Reference/FastNoiseLiteReference.cs` | Verbatim copy, used as the test oracle. |

The ports reproduce the reference **exactly**, not approximately. `CompatibilityTests` asserts
bit-for-bit equality between every kernel and the vendored original across the full configuration
matrix, so a world generated with FastNoiseLite keeps generating identically under this library.

### Licence

```
MIT License

Copyright(c) 2023 Jordan Peck (jordan.me2@gmail.com)
Copyright(c) 2023 Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files(the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions :

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
