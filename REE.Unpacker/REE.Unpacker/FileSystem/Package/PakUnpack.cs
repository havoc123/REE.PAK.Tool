using System;
using System.IO;
using System.Collections.Generic;

namespace REE.Unpacker
{
    class PakUnpack
    {
        private static Int32 dwEntrySize = 0;
        private static List<PakEntry> m_EntryTable = new List<PakEntry>();
        private static readonly Object s_SkipErrorLogLock = new Object();

        private static void iAppendSkippedHashToErrorLog(UInt64 dwEntryHash, String m_ListRelativePath)
        {
            try
            {
                String m_LogPath = Path.Combine(Utils.iGetApplicationPath(), "error_log.txt");
                String m_Line = dwEntryHash.ToString("X16");
                if (!String.IsNullOrEmpty(m_ListRelativePath))
                {
                    m_Line = m_Line + "\t" + m_ListRelativePath.Replace('\r', ' ').Replace('\n', ' ');
                }

                m_Line += Environment.NewLine;
                lock (s_SkipErrorLogLock)
                {
                    File.AppendAllText(m_LogPath, m_Line);
                }
            }
            catch
            {
            }
        }

        public static void iDoIt(String m_PakFile, String m_DstFolder, Boolean m_OnlyKnownInList)
        {
            using (FileStream TPakStream = new FileStream(m_PakFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan))
            {
                if (TPakStream.Length <= 16)
                {
                    Utils.iSetError("[ERROR]: Empty PAK archive file");
                    return;
                }

                var m_Header = new PakHeader();

                m_Header.dwMagic = TPakStream.ReadUInt32();
                m_Header.bMajorVersion = TPakStream.ReadByte();
                m_Header.bMinorVersion = TPakStream.ReadByte();
                m_Header.wFeature = (Features)TPakStream.ReadInt16();
                m_Header.dwTotalFiles = TPakStream.ReadInt32();
                m_Header.dwFingerprint = TPakStream.ReadUInt32();

                if (m_Header.dwMagic != 0x414B504B)
                {
                    Utils.iSetError("[ERROR]: Invalid magic of PAK archive file");
                    return;
                }

                if (m_Header.bMajorVersion != 2 && m_Header.bMajorVersion != 4 || m_Header.bMinorVersion != 0 && m_Header.bMinorVersion != 1 && m_Header.bMinorVersion != 2)
                {
                    Utils.iSetError("[ERROR]: Invalid version of PAK archive file -> " + m_Header.bMajorVersion.ToString() + "." + m_Header.bMinorVersion.ToString() + ", expected 2.0, 4.0, 4.1 & 4.2");
                    return;
                }

                if (m_Header.wFeature != Features.NONE && m_Header.wFeature != Features.ENCRYPTED_RESOURCES && m_Header.wFeature != Features.DLC_EXTRA_DATA1 && m_Header.wFeature != Features.EXTRA_DATA && m_Header.wFeature != Features.CHUNKED_RESOURCES && m_Header.wFeature != Features.DLC_EXTRA_DATA2)
                {
                    Utils.iSetError("[ERROR]: Archive is encrypted (obfuscated) with an unsupported algorithm or has unknown header flags");
                    return;
                }

                switch (m_Header.bMajorVersion)
                {
                    case 2: dwEntrySize = 24; break;
                    case 4: dwEntrySize = 48; break;
                    default: break;
                }

                Int64 lpTableByteCount = (Int64)m_Header.dwTotalFiles * dwEntrySize;
                if (lpTableByteCount < 0 || lpTableByteCount > int.MaxValue)
                {
                    Utils.iSetError("[ERROR]: Entry table size is invalid or too large for a single buffer");
                    return;
                }

                var lpTable = TPakStream.ReadBytes(lpTableByteCount);

                if (m_Header.wFeature == Features.ENCRYPTED_RESOURCES || m_Header.wFeature == Features.DLC_EXTRA_DATA1 || m_Header.wFeature == Features.EXTRA_DATA || m_Header.wFeature == Features.CHUNKED_RESOURCES || m_Header.wFeature == Features.DLC_EXTRA_DATA2)
                {
                    if (m_Header.wFeature == Features.EXTRA_DATA)
                    {
                        TPakStream.Seek(4, SeekOrigin.Current);
                    }
                    else if (m_Header.wFeature == Features.DLC_EXTRA_DATA1 || m_Header.wFeature == Features.DLC_EXTRA_DATA2)
                    {
                        TPakStream.Seek(9, SeekOrigin.Current);
                    }

                    var lpEncryptedKey = TPakStream.ReadBytes(128);

                    lpTable = PakCipher.iDecryptData(lpTable, lpEncryptedKey);

                    if (m_Header.wFeature == Features.CHUNKED_RESOURCES || m_Header.wFeature == Features.DLC_EXTRA_DATA2)
                    {
                        PakChunks.iReadMapTable(TPakStream);
                    }
                }

                m_EntryTable.Clear();
                using (var TEntryReader = new MemoryStream(lpTable))
                {
                    for (Int32 i = 0; i < m_Header.dwTotalFiles; i++)
                    {
                        var m_Entry = new PakEntry();

                        if (m_Header.bMajorVersion == 2 && m_Header.bMinorVersion == 0)
                        {
                            m_Entry.dwOffset = TEntryReader.ReadInt64();
                            m_Entry.dwDecompressedSize = TEntryReader.ReadInt64();
                            m_Entry.dwHashNameLower = TEntryReader.ReadUInt32();
                            m_Entry.dwHashNameUpper = TEntryReader.ReadUInt32();
                            m_Entry.dwCompressedSize = 0;
                            m_Entry.wCompressionType = 0;
                            m_Entry.dwChecksum = 0;
                        }
                        else if (m_Header.bMajorVersion == 4 && m_Header.bMinorVersion == 0 || m_Header.bMinorVersion == 1 || m_Header.bMinorVersion == 2)
                        {
                            m_Entry.dwHashNameLower = TEntryReader.ReadUInt32();
                            m_Entry.dwHashNameUpper = TEntryReader.ReadUInt32();
                            m_Entry.dwOffset = TEntryReader.ReadInt64();
                            m_Entry.dwCompressedSize = TEntryReader.ReadInt64();
                            m_Entry.dwDecompressedSize = TEntryReader.ReadInt64();
                            m_Entry.dwAttributes = TEntryReader.ReadInt64();
                            m_Entry.dwChecksum = TEntryReader.ReadUInt64();
                            m_Entry.wCompressionType = (Compression)(m_Entry.dwAttributes & 0xF);
                            m_Entry.wEncryptionType = (Encryption)((m_Entry.dwAttributes & 0x00FF0000) >> 16);
                        }
                        else
                        {
                            Utils.iSetError("[ERROR]: Something is wrong when reading the entry table");
                            return;
                        }

                        m_EntryTable.Add(m_Entry);
                    }
                }

                Int32 dwCounter = 1;
                foreach (var m_Entry in m_EntryTable)
                {
                    UInt64 dwEntryHash = ((UInt64)m_Entry.dwHashNameUpper << 32) | m_Entry.dwHashNameLower;
                    if (m_OnlyKnownInList && !PakList.iContainsHash(dwEntryHash))
                    {
                        Console.WriteLine("[跳过] 未知文件: " + dwEntryHash.ToString("X16"));
                        continue;
                    }

                    String m_FileName = PakList.iGetNameFromHashList(dwEntryHash);
                    String m_FullPath = m_DstFolder + m_FileName.Replace("/", @"\");

                    try
                    {
                        Console.Title = Program.m_Title + " - " + Path.GetFileName(m_PakFile) + " -> " + PakUtils.iPrintInfo(dwCounter++, (Int32)m_Header.dwTotalFiles);

                        Utils.iSetInfo("[UNPACKING]: " + m_FileName);
                        Utils.iCreateDirectory(m_FullPath);

                        TPakStream.Seek(m_Entry.dwOffset, SeekOrigin.Begin);
                        if (m_Entry.wCompressionType == Compression.NONE)
                        {
                            if (m_Header.wFeature == Features.CHUNKED_RESOURCES || m_Header.wFeature == Features.DLC_EXTRA_DATA2)
                            {
                                if (m_Entry.dwAttributes == 0x1000000 || m_Entry.dwAttributes == 0x1000400)
                                {
                                    var lpBuffer = PakChunks.iUnwrapChunks(TPakStream, m_Entry);

                                    m_FullPath = PakUtils.iDetectFileType(m_FullPath, lpBuffer);

                                    File.WriteAllBytes(m_FullPath, lpBuffer);
                                }
                                else
                                {
                                    var m_Chunks = PakUtils.iReadByChunks(TPakStream, m_Entry.dwCompressedSize);

                                    PakUtils.iWriteByChunks(m_FullPath, m_Chunks);
                                }
                            }
                            else
                            {
                                var m_Chunks = PakUtils.iReadByChunks(TPakStream, m_Entry.dwCompressedSize);

                                PakUtils.iWriteByChunks(m_FullPath, m_Chunks);
                            }
                        }
                        else if (m_Entry.wCompressionType == Compression.DEFLATE || m_Entry.wCompressionType == Compression.ZSTD)
                        {
                            var lpSrcBuffer = TPakStream.ReadBytes(m_Entry.dwCompressedSize);
                            var lpDstBuffer = new Byte[] { };

                            if (m_Entry.wEncryptionType != Encryption.None && m_Entry.wEncryptionType <= Encryption.Type_Invalid)
                            {
                                lpSrcBuffer = ResourceCipher.iDecryptResource(lpSrcBuffer);
                            }

                            switch (m_Entry.wCompressionType)
                            {
                                case Compression.DEFLATE: lpDstBuffer = DEFLATE.iDecompress(lpSrcBuffer); break;
                                case Compression.ZSTD: lpDstBuffer = ZSTD.iDecompress(lpSrcBuffer); break;
                            }

                            m_FullPath = PakUtils.iDetectFileType(m_FullPath, lpDstBuffer);

                            File.WriteAllBytes(m_FullPath, lpDstBuffer);
                        }
                        else
                        {
                            Utils.iSetError("[ERROR]: Unknown compression type detected -> " + m_Entry.wCompressionType.ToString());
                            return;
                        }
                    }
                    catch (OutOfMemoryException)
                    {
                        iAppendSkippedHashToErrorLog(dwEntryHash, m_FileName);
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[WARNING]: Skipped file (out of memory) — path: \"" + m_FullPath + "\", hash: 0x" + dwEntryHash.ToString("X16"));
                        Console.ResetColor();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        continue;
                    }
                    catch (Exception ex)
                    {
                        iAppendSkippedHashToErrorLog(dwEntryHash, m_FileName);
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[WARNING]: Skipped file — path: \"" + m_FullPath + "\", hash: 0x" + dwEntryHash.ToString("X16") + ", error: " + ex.Message);
                        Console.ResetColor();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        continue;
                    }
                }

                Console.Title = Program.m_Title;
            }
        }
    }
}
