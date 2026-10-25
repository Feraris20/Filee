# Archive fixtures

`ArchiveIsoTests` generates archives in code where a writer is available. These small upstream samples cover read-only formats and RAR encryption. Tests run offline; no downloads occur during tests. The DMG also supplies a raw FAT32 IMG input, extracted with 7-Zip's explicit DMG handler.

- libarchive, commit `4eed0032061e2370a2e8a87567b3e5a34927e2dd`, [libarchive/test](https://github.com/libarchive/libarchive/tree/4eed0032061e2370a2e8a87567b3e5a34927e2dd/libarchive/test), decoded from uuencoding:
  - `sample.rar`: `test_read_format_rar.rar.uu`
  - `sample-rar5.rar`: `test_read_format_rar5_compressed.rar.uu`
  - `sample.cab`: `test_read_format_cab_1.cab.uu`
  - `sample.lzh`: `test_read_format_lha_lh0.lzh.uu`
  - `sample.rpm`: `test_read_format_cpio_svr4_gzip_rpm.rpm.uu`
  - `sample.tar.Z`: `test_read_format_gtar_filename_koi8r.tar.Z.uu`
  - `locked-rar4.rar`: `test_read_format_rar4_encrypted.rar.uu`
  - `locked-rar4-headers.rar`: `test_read_format_rar4_encrypted_filenames.rar.uu`
  - `locked-rar5.rar`: `test_read_format_rar5_encrypted.rar.uu`
  - `locked-rar5-headers.rar`: `test_read_format_rar5_encrypted_filenames.rar.uu`
  - License: `libarchive-COPYING`.
- `sample.arj`: SevenZipExtractor, commit `95c469c9554ffa5218f8771ded9dc4b58dba8819`, [ansimate-arj.arj](https://github.com/adoconnection/SevenZipExtractor/blob/95c469c9554ffa5218f8771ded9dc4b58dba8819/SevenZipExtractor.Tests/Resources/ansimate-arj.arj). License: `SevenZipExtractor-LICENSE`.
- `sample.dmg`: apple-platform-rs, commit `0ebbd2ef3b96d5adccc30b1cad4deea38cd735c4`, [example.dmg](https://github.com/indygreg/apple-platform-rs/blob/0ebbd2ef3b96d5adccc30b1cad4deea38cd735c4/apple-dmg/assets/example.dmg). License: `apple-platform-LICENSE-MIT`.

The original archive is extracted for an expected snapshot of paths, SHA-256 file contents and empty directories. The ISO is independently extracted by 7-Zip and compared with that snapshot. Source bytes must remain unchanged.
