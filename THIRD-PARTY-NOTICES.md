# Third-party notices

Droid Hero is released under the MIT licence (see [LICENSE](LICENSE)). It also contains the
component listed below, which carries its own licence. That licence requires its copyright notice
and permission text to be reproduced wherever the component is redistributed in binary form, so
the full text is reproduced here.

No music and no charts are included in this repository or in any APK built from it. Charts are
downloaded on demand from [enchor.us](https://www.enchor.us/); audio is read from the player's own
device. Neither is redistributed.

---

## Concentus

| | |
| --- | --- |
| Component | Concentus — managed C# implementation of the Opus audio codec |
| Version | 1.1.6 |
| Upstream | https://github.com/lostromb/concentus |
| File | `DroidHero/Assets/Plugins/Concentus/Concentus.dll` |
| Used for | Decoding `song.opus` from `.sng` charts, which Unity cannot decode natively |
| Licence | BSD-3-Clause style (Opus/Concentus terms, reproduced below) |

Concentus is a port of the Opus reference implementation, so the notice below covers both. It is
redistributed here unmodified, as a compiled assembly.

```
Copyright (c) by various holding parties, including (but not limited to):
Skype Limited, Xiph.Org Foundation, CSIRO, Microsoft Corporation,
Jean-Marc Valin, Gregory Maxwell, Mark Borgerding, Timothy B. Terriberry,
Logan Stromberg. All rights are reserved by their respective holders.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


This repository and its redistributable packages contain independently compiled
versions of the Opus C reference library, which is maintained by Xiph.org and the
Opus open-source contributors. The source code for these libraries is freely available
at https://gitlab.xiph.org/xiph/opus/-/tags/v1.5.2, and all binaries are being
redistributed to you under the same terms of the general Opus license dictated above.
```

---

## Unity engine

APKs built from this project embed the Unity runtime. That runtime is not part of this repository
and is not covered by the MIT licence above; it is governed by the
[Unity Terms of Service](https://unity.com/legal/terms-of-service) and the terms of whichever Unity
licence the build was activated under.

The Unity modules referenced in `DroidHero/Packages/manifest.json` (`androidjni`, `audio`,
`imageconversion`, `imgui`, `jsonserialize`, `ui`, `unitywebrequest`, `unitywebrequestaudio`,
`unitywebrequesttexture`) are part of that engine and ship under those same terms.

## AndroidX and Gradle dependencies

The APK links AndroidX libraries pulled in by Unity's Gradle template. They are Apache-2.0
licensed, are not stored in this repository, and are resolved at build time from Google's Maven
repository. Their notices ship with the build toolchain rather than with this source tree.

---

## If you redistribute a build

Keep this file with any binary you publish. The Concentus terms above are the only ones that
impose a documentation obligation on a redistributed APK, and dropping this file would put a build
out of compliance with them.
