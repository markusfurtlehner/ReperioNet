# Third-party notices

ReperioNet is licensed under the MIT license (see [LICENSE](LICENSE)). It incorporates material
derived from the third-party projects listed below; the corresponding notices and license terms
are reproduced here as required by their licenses.

---

## Snowball (stemming algorithms)

The stemmer classes in the `ReperioNet.Languages.*` packages
(`Snowball<Language>Stemmer` in each pack) are pure-managed C# ports of the official Snowball
stemming algorithms published by the Snowball project (<https://snowballstem.org>,
<https://github.com/snowballstem/snowball>), and several of the bundled stop-word lists are derived
from the stop-word lists distributed by the same project. These are derivative works of material
covered by the following license:

```
Copyright (c) 2001, Dr Martin Porter
Copyright (c) 2002, Richard Boulton
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice,
   this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the Snowball project nor the names of its contributors
   may be used to endorse or promote products derived from this software
   without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```

---

## Apache Lucene (Turkish stop-word list)

The Turkish stop-word list in `ReperioNet.Languages.Tr` is curated from the Turkish stop-word list
distributed with Apache Lucene (<https://lucene.apache.org>).

```
Copyright The Apache Software Foundation

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

---

## NTextCat (Core14 language profile)

`ReperioNet.LanguageDetection` bundles the `Core14.profile.xml` language profile from the NTextCat
project (<https://github.com/ivanakcheurov/ntextcat>), (c) Ivan Akcheurov, distributed under the
MIT license:

```
MIT License

Copyright (c) Ivan Akcheurov

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Algorithms implemented from published descriptions

The Kölner Phonetik encoder (`ReperioNet.Languages.De`) implements the procedure published by
H. J. Postel (1969), and the Double Metaphone encoder (`ReperioNet.Languages.En`) implements the
algorithm published by Lawrence Philips (2000). Both are original ReperioNet implementations of the
published algorithm descriptions; no third-party source code was incorporated.
