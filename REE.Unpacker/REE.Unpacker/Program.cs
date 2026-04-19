using System;
using System.Collections.Generic;
using System.IO;

namespace REE.Unpacker
{
    class Program
    {
        public static String m_Title = "RE Engine PAK Unpacker";

        static void Main(String[] args)
        {
            Console.Title = m_Title;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(m_Title);
            Console.WriteLine("(c) 2023-2026 Ekey (h4x0r) / v{0}\n", Utils.iGetApplicationVersion());
            Console.ResetColor();

            var m_Positional = new List<String>();
            Boolean m_OnlyKnownInList = false;

            foreach (String m_Arg in args)
            {
                if (String.Equals(m_Arg, "--known-only", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(m_Arg, "-knownonly", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(m_Arg, "/knownonly", StringComparison.OrdinalIgnoreCase))
                {
                    m_OnlyKnownInList = true;
                }
                else
                {
                    m_Positional.Add(m_Arg);
                }
            }

            if (m_Positional.Count != 2 && m_Positional.Count != 3)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("[Usage]");
                Console.WriteLine("    REE.Unpacker <m_ProjectFile> <m_File> [m_Directory] [--known-only]\n");
                Console.WriteLine("    m_ProjectFile - Project file (Tag) with filenames (file must be in Projects folder)");
                Console.WriteLine("    m_File - Source of PAK archive file");
                Console.WriteLine("    m_Directory - Destination directory (Optional)");
                Console.WriteLine("    --known-only  - Skip entries whose hash is not in the project list (optional flag, any position)\n");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Examples]");
                Console.WriteLine("    REE.Unpacker MHR_PC_DEMO E:\\Games\\MHR\\re_chunk_000.pak");
                Console.WriteLine("    REE.Unpacker MHR_PC_DEMO E:\\Games\\MHR\\re_chunk_000.pak D:\\Unpacked");
                Console.WriteLine("    REE.Unpacker MHR_PC_DEMO E:\\Games\\MHR\\re_chunk_000.pak --known-only");
                Console.WriteLine("    REE.Unpacker MHR_PC_DEMO E:\\Games\\MHR\\re_chunk_000.pak D:\\Unpacked --known-only");
                Console.ResetColor();
                return;
            }

            String m_ListFile = m_Positional[0];
            String m_PakFile = m_Positional[1];
            String m_Output = null;

            if (m_Positional.Count == 2)
            {
                m_Output = Path.GetDirectoryName(m_Positional[1]) + @"\" + Path.GetFileNameWithoutExtension(m_Positional[1]) + @"\";
            }
            else
            {
                m_Output = Utils.iCheckArgumentsPath(m_Positional[2]);
            }

            if (!File.Exists("Zstandard.Net.dll") || !File.Exists("libzstd.dll"))
            {
                Utils.iSetError("[ERROR]: Unable to find ZSTD modules");
                return;
            }

            if (!File.Exists(m_PakFile))
            {
                Utils.iSetError("[ERROR]: Input PAK file -> " + m_PakFile + " <- does not exist");
                return;
            }

            PakList.iLoadProject(m_ListFile);
            PakUnpack.iDoIt(m_PakFile, m_Output, m_OnlyKnownInList);
        }
    }
}
