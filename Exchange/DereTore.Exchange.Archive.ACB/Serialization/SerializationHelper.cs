using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using DereTore.Common;

namespace DereTore.Exchange.Archive.ACB.Serialization {
    internal static class SerializationHelper {

        public static uint RoundUpAsTable(uint value, uint alignment) {
            // This action seems weird. But it does exist (see Cue table in CGSS song_1001[oneshin]), I don't know why.
            value = AcbHelper.RoundUpToAlignment(value, 4);

            if (value % alignment == 0) {
                value += alignment;
            }

            return AcbHelper.RoundUpToAlignment(value, alignment);
        }

        public static MemberAbstract[] GetSearchTargetFieldsAndProperties(UtfRowBase tableObject) {
            var type = tableObject.GetType();
            var objFields = type.GetFields(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic);
            var validDescriptors = new List<MemberAbstract>();
            var lastOrder = -1;

            foreach (var field in objFields) {
                var utfFieldAttribute = GetCustomAttribute<UtfFieldAttribute>(field);

                // It is a field that needs serialization.
                if (utfFieldAttribute != null) {
                    var afs2ArchiveAttribute = GetCustomAttribute<Afs2ArchiveAttribute>(field);

                    if (utfFieldAttribute.Order < 0) {
                        ++lastOrder;
                        utfFieldAttribute.Order = lastOrder;
                    } else {
                        lastOrder = utfFieldAttribute.Order;
                    }

                    validDescriptors.Add(new MemberAbstract(field, utfFieldAttribute, afs2ArchiveAttribute));
                }
            }

            validDescriptors.Sort((d1, d2) => d1.FieldAttribute.Order.CompareTo(d2.FieldAttribute.Order));

            return validDescriptors.ToArray();
        }

        public static byte[] GetAfs2ArchiveBytes(ReadOnlyCollection<byte[]> files, uint alignment) {
            if (files.Count == 0) {
                return new byte[0];
            }

            byte[] buffer;

            using (var memory = new MemoryStream()) {
                WriteAfs2ArchiveToStream(files, memory, alignment);
                buffer = memory.ToArray();
            }

            return buffer;
        }

        public static T GetCustomAttribute<T>(Type type, bool inherit = false) where T : Attribute {
            var attr = type.GetCustomAttributes(typeof(T), inherit);

            if (attr.Length == 0) {
                return null;
            } else {
                return attr[0] as T;
            }
        }

        public static T GetCustomAttribute<T>(FieldInfo fieldInfo, bool inherit = false) where T : Attribute {
            var attr = fieldInfo.GetCustomAttributes(typeof(T), inherit);

            if (attr.Length == 0) {
                return null;
            } else {
                return attr[0] as T;
            }
        }

        private static void WriteAfs2ArchiveToStream(ReadOnlyCollection<byte[]> files, Stream stream, uint alignment) {
            var fileCount = (uint)files.Count;
            if (files.Count >= ushort.MaxValue) {
                throw new IndexOutOfRangeException($"File count {fileCount} exceeds maximum possible value (65535).");
            }
            if (files.Count != 1) {
                throw new NotSupportedException("Currently DereTore does not support more than one file.");
            }
            stream.WriteBytes(Afs2Archive.Afs2Signature);
            const uint version = 0x00020401;
            stream.WriteUInt32LE(version);
            stream.WriteUInt32LE(fileCount);
            stream.WriteUInt32LE(alignment);
            const uint offsetFieldSize = (version >> 8) & 0xff; // version[1], always 4? See Afs2Archive.Initialize().

            // Prepare the fields.
            var afs2HeaderSegmentSize = 0x10 + // General data
                                        2 * fileCount + // Cue IDs
                                        offsetFieldSize * fileCount + // File offsets
                                        sizeof(uint); // Size of last file (U32)
            // Assuming the music file always has ID 0 in Waveform table and Cue table.
            var records = new List<Afs2FileRecord>();
            var currentFileRawOffset = afs2HeaderSegmentSize;
            for (ushort i = 0; i < fileCount; ++i) {
                var record = new Afs2FileRecord {
                    // TODO: Use the Cue table.
                    CueId = i,
                    FileOffsetRaw = currentFileRawOffset,
                    FileOffsetAligned = AcbHelper.RoundUpToAlignment(currentFileRawOffset, alignment)
                };
                records.Add(record);
                currentFileRawOffset = (uint)(record.FileOffsetAligned + files[i].Length);
            }

            var lastFileEndOffset = currentFileRawOffset;
            for (var i = 0; i < files.Count; ++i) {
                stream.WriteUInt16LE(records[i].CueId);
            }
            for (var i = 0; i < files.Count; ++i) {
                stream.WriteUInt32LE((uint)records[i].FileOffsetRaw);
            }
            // TODO: Dynamically judge it. See Afs2Archive.Initialize().
            stream.WriteUInt32LE(lastFileEndOffset);
            for (var i = 0; i < files.Count; ++i) {
                stream.SeekAndWriteBytes(files[i], records[i].FileOffsetAligned);
            }
        }

    }
}
