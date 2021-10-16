using System;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using Memoria.Prime;
using Memoria.Prime.AKB2;
using Memoria.Prime.NVorbis;

public class SoundLoaderResources : ISoundLoader
{
	public override void Initial()
	{
	}

	public override void Load(SoundProfile profile, ISoundLoader.ResultCallback callback, SoundDatabase soundDatabase)
	{
		String akbPath = "Sounds/" + profile.ResourceID + ".akb";
		String[] akbInfo;
		Byte[] binAsset = AssetManager.LoadBytes(akbPath, out akbInfo);
		SoundLib.Log("Load: " + akbPath);
		if (binAsset == null)
		{
			String oggPath = AssetManager.SearchAssetOnDisc("Sounds/" + profile.ResourceID + ".ogg", true, false);
			if (!String.IsNullOrEmpty(oggPath))
			{
				try
				{
					Byte[] binOgg = File.ReadAllBytes(oggPath);
					binAsset = ReadAkbDataFromOgg(profile, binOgg);
					File.WriteAllBytes(Path.ChangeExtension(oggPath, ".akb.bytes"), binAsset);
				}
				catch (Exception err)
				{
					Log.Error(err, $"[{nameof(SoundLoaderResources)}] Load {oggPath} failed.");
					callback((SoundProfile)null, (SoundDatabase)null);
					return;
				}
			}
			else
			{
				SoundLib.LogError("File not found AT path: " + akbPath);
				callback((SoundProfile)null, (SoundDatabase)null);
				return;
			}
		}
		// if (((binAsset[0] << 24) | (binAsset[1] << 16) | (binAsset[2] << 8) | binAsset[3]) == 0x4F676753)
		IntPtr intPtr = Marshal.AllocHGlobal((Int32)binAsset.Length);
		Marshal.Copy(binAsset, 0, intPtr, (Int32)binAsset.Length);
		if (akbInfo.Length > 0)
        {
			// Assume that AKB header is always of size 304 (split "intPtr" into AkbBin + OggBin if ever that changes)
			// Maybe use a constant instead of 304 ("SoundImporter.AkbHeaderSize" or something defined at a better place?)
			Byte[] akbBin = new byte[304];
			Marshal.Copy(intPtr, akbBin, 0, 304);
			AKB2Header akbHeader = new AKB2Header();
			akbHeader.ReadFromBytes(akbBin);
			foreach (String s in akbInfo)
            {
				String[] akbCode = s.Split(' ');
				if (akbCode.Length >= 2 && String.Compare(akbCode[0], "LoopStart") == 0)
					UInt32.TryParse(akbCode[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out akbHeader.LoopStart);
				else if (akbCode.Length >= 2 && String.Compare(akbCode[0], "LoopEnd") == 0)
					UInt32.TryParse(akbCode[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out akbHeader.LoopEnd);
				else if (akbCode.Length >= 2 && String.Compare(akbCode[0], "LoopStart2") == 0)
					UInt32.TryParse(akbCode[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out akbHeader.LoopStartAlternate);
				else if (akbCode.Length >= 2 && String.Compare(akbCode[0], "LoopEnd2") == 0)
					UInt32.TryParse(akbCode[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out akbHeader.LoopEndAlternate);
				else if (akbCode.Length >= 2 && String.Compare(akbCode[0], "SampleRate") == 0)
					UInt16.TryParse(akbCode[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out akbHeader.SampleRate);
			}
			Marshal.Copy(akbHeader.WriteToBytes(), 0, intPtr, 304);
		}
		Int32 bankID = ISdLibAPIProxy.Instance.SdSoundSystem_AddData(intPtr);
		profile.AkbBin = intPtr;
		profile.BankID = bankID;
		callback(profile, soundDatabase);
	}

	// Duplicate of SoundImporter.ReadAkbDataFromOgg
	public static Byte[] ReadAkbDataFromOgg(SoundProfile profile, Byte[] oggBinary)
	{
		using (BinaryReader input = new BinaryReader(new MemoryStream(oggBinary)))
		{
			// Get size of content
			UInt32 contentSize = (UInt32)oggBinary.Length;
			UInt32 tailSize = contentSize % 16;
			if (tailSize > 0)
				tailSize = 16 - tailSize;
			UInt32 dataSize = AkbHeaderSize + contentSize;
			UInt32 resultSize = dataSize + tailSize;
			Byte[] akbBinary = new Byte[resultSize];

			// Prepare header
			unsafe
			{
				fixed (Byte* fixedBinary = akbBinary)
				{
					AKB2Header* header = (AKB2Header*)fixedBinary;
					AKB2Header.Initialize(header);

					// Initialize header
					header->FileSize = resultSize;
					header->ContentSize = contentSize;
					header->Unknown09 = 0x000004002;
					header->Unknown33 = 0x0002;
					ReadMetadataFromVorbis(profile, (MemoryStream)input.BaseStream, header);
				}
			}

			// Copy OGG
			Array.Copy(oggBinary, 0, akbBinary, AkbHeaderSize, contentSize);
			return akbBinary;
		}
	}

	private static unsafe void ReadMetadataFromVorbis(SoundProfile profile, MemoryStream input, AKB2Header* header)
	{
		input.Position = 0;

		using (VorbisReader vorbis = new VorbisReader(input, false))
		{
			//header->SampleCount = checked((UInt32)vorbis.TotalSamples);
			header->SampleRate = checked((UInt16)vorbis.SampleRate);

			foreach (String comment in vorbis.Comments)
			{
				TryParseTag(comment, "LoopStart", ref header->LoopStart);
				TryParseTag(comment, "LoopEnd", ref header->LoopEnd);
				TryParseTag(comment, "LoopStart2", ref header->LoopStartAlternate);
				TryParseTag(comment, "LoopEnd2", ref header->LoopEndAlternate);
			}

			if (profile.SoundProfileType == SoundProfileType.Music)
			{
				if (header->LoopEnd == 0)
					header->LoopEnd = checked((UInt32)(vorbis.TotalSamples - 1));
			}
		}
	}

	private static Boolean TryParseTag(String comment, String tagName, ref UInt32 tagVariable)
	{
		String tagWithEq = tagName + "=";
		if (comment.Length > tagWithEq.Length && comment.StartsWith(tagWithEq, StringComparison.InvariantCultureIgnoreCase))
		{
			tagVariable = UInt32.Parse(comment.Substring(tagWithEq.Length), CultureInfo.InvariantCulture);
			return true;
		}
		return false;
	}

	private const Int32 AkbHeaderSize = 304;
}
