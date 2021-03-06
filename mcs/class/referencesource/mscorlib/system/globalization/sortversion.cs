using System;
using System.Diagnostics.Contracts;

namespace System.Globalization {

    [Serializable]
    public sealed class SortVersion : IEquatable<SortVersion> {

        private int m_NlsVersion;
        private Guid m_SortId;

        public int FullVersion {
            get {
                return m_NlsVersion;
            }
        }

        public Guid SortId {
            get {
                return m_SortId;
            }
        }

        public SortVersion(int fullVersion, Guid sortId) {           
            m_SortId = sortId;
            m_NlsVersion = fullVersion;
        }

        internal SortVersion(int nlsVersion, int effectiveId, Guid customVersion) {
            m_NlsVersion = nlsVersion;

            if(customVersion == Guid.Empty) {
#if !MONO                
                byte[] b = BitConverter.GetBytes(effectiveId);
#endif
                byte b1 = (byte) ((uint) effectiveId >> 24);
                byte b2 = (byte) ((effectiveId  & 0x00FF0000) >> 16);
                byte b3 = (byte) ((effectiveId  & 0x0000FF00) >> 8);
                byte b4 = (byte) (effectiveId  & 0xFF);
                customVersion = new Guid(0,0,0,0,0,0,0,b1,b2,b3,b4);
            }

            m_SortId = customVersion;
        }

        public override bool Equals(object obj) {
            SortVersion n = obj as SortVersion;
            if(n != null) {
                return this.Equals(n);
            }

            return false;
        }

        public bool Equals(SortVersion other) {
            if(other == null) {
                return false;
            }

            return m_NlsVersion == other.m_NlsVersion && m_SortId == other.m_SortId;
        }

        public override int GetHashCode() { 
            return m_NlsVersion * 7 | m_SortId.GetHashCode(); 
        }

        public static bool operator ==(SortVersion left, SortVersion right) {
            if (((object) left) != null) {
                return left.Equals(right);
            }

            if (((object) right) != null) {
                return right.Equals(left);
            }

            // Both null.
            return true;
        }

        public static bool operator !=(SortVersion left, SortVersion right) {
            return !(left == right);
        }
    }
}
