using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace CustomSosigReplacer
{
    internal sealed class RuntimeBoneClip
    {
        public readonly string Name;
        public readonly float Duration;
        public readonly float FramesPerSecond;
        private readonly int _frameCount;
        private readonly BoneTrack[] _tracks;

        private RuntimeBoneClip(string name, float duration, float framesPerSecond, int frameCount, BoneTrack[] tracks)
        {
            Name = name;
            Duration = duration;
            FramesPerSecond = framesPerSecond;
            _frameCount = frameCount;
            _tracks = tracks;
        }

        public static RuntimeBoneClip Load(string path, int skeletonBoneCount)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Retargeted bone animation was not found.", path);
            using (FileStream file = File.OpenRead(path))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(gzip))
            {
                string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (magic != "CSA1") throw new IOException("Unexpected bone animation signature: " + magic);
                int version = reader.ReadInt32();
                if (version != 1) throw new IOException("Unsupported bone animation version: " + version);
                string name = ReadString(reader);
                float duration = reader.ReadSingle();
                float fps = reader.ReadSingle();
                int frameCount = reader.ReadInt32();
                int trackCount = reader.ReadInt32();
                if (duration <= 0f || fps <= 0f || frameCount < 2 || frameCount > 10000) throw new IOException("Invalid bone animation timing.");
                if (trackCount <= 0 || trackCount > skeletonBoneCount) throw new IOException("Invalid bone animation track count: " + trackCount);
                BoneTrack[] tracks = new BoneTrack[trackCount];
                for (int trackIndex = 0; trackIndex < trackCount; trackIndex++)
                {
                    int boneIndex = reader.ReadInt32();
                    if (boneIndex < 0 || boneIndex >= skeletonBoneCount) throw new IOException("Animation references invalid bone " + boneIndex);
                    Quaternion[] rotations = new Quaternion[frameCount];
                    for (int frame = 0; frame < frameCount; frame++)
                        rotations[frame] = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    tracks[trackIndex] = new BoneTrack(boneIndex, rotations);
                }
                return new RuntimeBoneClip(name, duration, fps, frameCount, tracks);
            }
        }

        public void Sample(float time, Transform[] bones, float weight)
        {
            float frame = Mathf.Repeat(time, Duration) * FramesPerSecond;
            ApplyFrame(frame, bones, weight, true, null);
        }

        internal Quaternion FirstRotation(int boneIndex, Quaternion fallback)
        {
            for (int index = 0; index < _tracks.Length; index++)
                if (_tracks[index].BoneIndex == boneIndex) return _tracks[index].Rotations[0];
            return fallback;
        }

        // Add the authored change relative to a reference, retaining the live
        // gait rather than replacing it with a static pose on every gunshot.
        public void SampleAdditive(float time, Transform[] bones, float weight, bool[] mask, RuntimeBoneClip reference, bool loop)
        {
            float frame = (loop ? Mathf.Repeat(time, Duration) : Mathf.Clamp(time, 0f, Duration)) * FramesPerSecond;
            int first = Mathf.Clamp(Mathf.FloorToInt(frame), 0, _frameCount - 1);
            int second = Mathf.Min(first + 1, _frameCount - 1);
            float blend = frame - Mathf.Floor(frame);
            for (int index = 0; index < _tracks.Length; index++)
            {
                BoneTrack track = _tracks[index];
                int boneIndex = track.BoneIndex;
                if (boneIndex >= bones.Length || bones[boneIndex] == null || (mask != null && !mask[boneIndex])) continue;
                Quaternion rest = reference.FirstRotation(boneIndex, track.Rotations[0]);
                Quaternion sampled = Quaternion.Slerp(track.Rotations[first], track.Rotations[second], blend);
                Quaternion delta = Quaternion.Inverse(rest) * sampled;
                bones[boneIndex].localRotation *= Quaternion.Slerp(Quaternion.identity, delta, Mathf.Clamp01(weight));
            }
        }

        public void SampleClamped(float time, Transform[] bones, float weight)
        {
            float frame = Mathf.Clamp(time, 0f, Duration) * FramesPerSecond;
            ApplyFrame(frame, bones, weight, false, null);
        }

        public void SampleMasked(float time, Transform[] bones, float weight, bool[] boneMask)
        {
            float frame = Mathf.Repeat(time, Duration) * FramesPerSecond;
            ApplyFrame(frame, bones, weight, true, boneMask);
        }

        public void SampleClampedMasked(float time, Transform[] bones, float weight, bool[] boneMask)
        {
            float frame = Mathf.Clamp(time, 0f, Duration) * FramesPerSecond;
            ApplyFrame(frame, bones, weight, false, boneMask);
        }

        private void ApplyFrame(float frame, Transform[] bones, float weight, bool loop, bool[] boneMask)
        {
            int first = Mathf.Clamp(Mathf.FloorToInt(frame), 0, _frameCount - 1);
            int second = loop ? (first + 1) % _frameCount : Mathf.Min(first + 1, _frameCount - 1);
            float blend = frame - Mathf.Floor(frame);
            float influence = Mathf.Clamp01(weight);
            for (int index = 0; index < _tracks.Length; index++)
            {
                BoneTrack track = _tracks[index];
                if (track.BoneIndex >= bones.Length || bones[track.BoneIndex] == null) continue;
                if (boneMask != null && (track.BoneIndex >= boneMask.Length || !boneMask[track.BoneIndex])) continue;
                Quaternion sampled = Quaternion.Slerp(track.Rotations[first], track.Rotations[second], blend);
                bones[track.BoneIndex].localRotation = Quaternion.Slerp(bones[track.BoneIndex].localRotation, sampled, influence);
            }
        }

        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length <= 0 || length > 4096) throw new IOException("Invalid string length in bone animation: " + length);
            return Encoding.UTF8.GetString(reader.ReadBytes(length));
        }

        private sealed class BoneTrack
        {
            public readonly int BoneIndex;
            public readonly Quaternion[] Rotations;
            public BoneTrack(int boneIndex, Quaternion[] rotations) { BoneIndex = boneIndex; Rotations = rotations; }
        }
    }
}
