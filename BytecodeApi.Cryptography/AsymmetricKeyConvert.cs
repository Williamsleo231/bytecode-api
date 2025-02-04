using BytecodeApi.Extensions;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BytecodeApi.Cryptography
{
	/// <summary>
	/// Class that converts between different RSA key formats, i.e. DER, PEM and the <see cref="RSAParameters" /> structure. Public keys are handled using the PKCS#8 format and private using the PKCS#1 format.
	/// </summary>
	public static class AsymmetricKeyConvert
	{
		//TODO: "BEGIN RSA PUBLIC KEY" and "BEGIN PRIVATE KEY"
		private const string PublicKeyHeader = "-----BEGIN PUBLIC KEY-----";
		private const string PublicKeyFooter = "-----END PUBLIC KEY-----";
		private const string PrivateKeyHeader = "-----BEGIN RSA PRIVATE KEY-----";
		private const string PrivateKeyFooter = "-----END RSA PRIVATE KEY-----";

		/// <summary>
		/// Converts a public or private RSA key, represented in PEM format to its equivalent DER representation.
		/// </summary>
		/// <param name="pemKey">A <see cref="string" /> value with the PEM key, starting with "-----BEGIN PUBLIC KEY-----" or "-----BEGIN RSA PRIVATE KEY-----".</param>
		/// <returns>
		/// A new <see cref="byte" />[] with the converted key.
		/// </returns>
		public static byte[] ToDer(string pemKey)
		{
			Check.ArgumentNull(pemKey, nameof(pemKey));

			pemKey = pemKey.Trim();
			bool isPublic = pemKey.StartsWith(PublicKeyHeader) && pemKey.EndsWith(PublicKeyFooter);
			bool isPrivate = pemKey.StartsWith(PrivateKeyHeader) && pemKey.EndsWith(PrivateKeyFooter);

			if (isPublic || isPrivate)
			{
				if (isPublic) pemKey = pemKey.ReplaceMultiple(null, PublicKeyHeader, PublicKeyFooter);
				else pemKey = pemKey.ReplaceMultiple(null, PrivateKeyHeader, PrivateKeyFooter);

				return Convert.FromBase64String(pemKey);
			}
			else
			{
				throw CreateFormatException();
			}
		}
		/// <summary>
		/// Converts a public or private RSA key from a <see cref="RSAParameters" /> structure to its equivalent DER representation.
		/// </summary>
		/// <param name="key">A <see cref="RSAParameters" /> structure with the key.</param>
		/// <param name="keyType">An <see cref="AsymmetricKeyType" /> value specifying whether to create a public or a private RSA key <see cref="byte" />[].</param>
		/// <returns>
		/// A new <see cref="byte" />[] with the converted key.
		/// </returns>
		public static byte[] ToDer(RSAParameters key, AsymmetricKeyType keyType)
		{
			using (MemoryStream memoryStream = new MemoryStream())
			{
				using (BinaryWriter writer = new BinaryWriter(memoryStream))
				{
					writer.Write((byte)0x30);

					using (MemoryStream innerStream = new MemoryStream())
					using (BinaryWriter innerWriter = new BinaryWriter(innerStream))
					{
						if (keyType == AsymmetricKeyType.Public)
						{
							innerWriter.Write((byte)0x30);
							EncodeLength(innerWriter, 13);
							innerWriter.Write((byte)6);

							byte[] oid = { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01 };
							EncodeLength(innerWriter, oid.Length);
							innerWriter.Write(oid);

							innerWriter.Write((byte)5);
							EncodeLength(innerWriter, 0);
							innerWriter.Write((byte)3);

							using (MemoryStream bitStringStream = new MemoryStream())
							using (BinaryWriter bitStringWriter = new BinaryWriter(bitStringStream))
							{
								bitStringWriter.Write((byte)0);
								bitStringWriter.Write((byte)0x30);

								using (MemoryStream paramsStream = new MemoryStream())
								using (BinaryWriter paramsWriter = new BinaryWriter(paramsStream))
								{
									EncodeIntegerBigEndian(paramsWriter, key.Modulus);
									EncodeIntegerBigEndian(paramsWriter, key.Exponent);

									EncodeLength(bitStringWriter, (int)paramsStream.Length);
									paramsStream.WriteTo(bitStringStream);
								}

								EncodeLength(innerWriter, (int)bitStringStream.Length);
								bitStringStream.WriteTo(innerStream);
							}
						}
						else if (keyType == AsymmetricKeyType.Private)
						{
							EncodeIntegerBigEndian(innerWriter, new byte[] { 0 });
							EncodeIntegerBigEndian(innerWriter, key.Modulus);
							EncodeIntegerBigEndian(innerWriter, key.Exponent);
							EncodeIntegerBigEndian(innerWriter, key.D);
							EncodeIntegerBigEndian(innerWriter, key.P);
							EncodeIntegerBigEndian(innerWriter, key.Q);
							EncodeIntegerBigEndian(innerWriter, key.DP);
							EncodeIntegerBigEndian(innerWriter, key.DQ);
							EncodeIntegerBigEndian(innerWriter, key.InverseQ);
						}
						else
						{
							throw Throw.InvalidEnumArgument(nameof(keyType), keyType);
						}

						EncodeLength(writer, (int)innerStream.Length);
						innerStream.WriteTo(memoryStream);
					}
				}

				return memoryStream.ToArray();
			}

			void EncodeLength(BinaryWriter encodeWriter, int length)
			{
				if (length < 128)
				{
					encodeWriter.Write((byte)length);
				}
				else
				{
					int bytesRequired;
					for (bytesRequired = 0; length >> (bytesRequired << 3) > 0; bytesRequired++)
					{
					}

					encodeWriter.Write((byte)(bytesRequired | 128));

					for (int i = bytesRequired - 1; i >= 0; i--)
					{
						encodeWriter.Write((byte)(length >> (i << 3) & 0xff));
					}
				}
			}
			void EncodeIntegerBigEndian(BinaryWriter encodeWriter, byte[] value)
			{
				encodeWriter.Write((byte)2);

				int prefixZeros = 0;
				for (int i = 0; i < value.Length && value[i] == 0; i++, prefixZeros++)
				{
				}

				if (prefixZeros == value.Length)
				{
					EncodeLength(encodeWriter, 1);
					encodeWriter.Write((byte)0);
				}
				else
				{
					if (value[prefixZeros] > 0x7f)
					{
						EncodeLength(encodeWriter, value.Length - prefixZeros + 1);
						encodeWriter.Write((byte)0);
					}
					else
					{
						EncodeLength(encodeWriter, value.Length - prefixZeros);
					}

					for (int i = prefixZeros; i < value.Length; i++)
					{
						encodeWriter.Write(value[i]);
					}
				}
			}
		}
		/// <summary>
		/// Converts a public or private RSA key, represented in DER format to its equivalent PEM representation.
		/// </summary>
		/// <param name="derKey">A <see cref="byte" />[] with the DER key in its binary representation.</param>
		/// <param name="keyType">An <see cref="AsymmetricKeyType" /> value specifying whether to create a public or a private RSA key <see cref="string" />.</param>
		/// <returns>
		/// The equivalent PEM key, represented as a <see cref="string" />.
		/// </returns>
		public static string ToPem(byte[] derKey, AsymmetricKeyType keyType)
		{
			Check.ArgumentNull(derKey, nameof(derKey));

			StringBuilder pemKey = new StringBuilder();
			pemKey.AppendLine(keyType == AsymmetricKeyType.Public ? PublicKeyHeader : PrivateKeyHeader);

			foreach (string line in Convert.ToBase64String(derKey).SplitToChunks(64))
			{
				pemKey.AppendLine(line);
			}

			pemKey.AppendLine(keyType == AsymmetricKeyType.Public ? PublicKeyFooter : PrivateKeyFooter);
			return pemKey.ToString();
		}
		/// <summary>
		/// Converts a public or private RSA key from a <see cref="RSAParameters" /> structure to its equivalent PEM representation.
		/// </summary>
		/// <param name="key">A <see cref="RSAParameters" /> structure with the key.</param>
		/// <param name="keyType">An <see cref="AsymmetricKeyType" /> value specifying whether to create a public or a private RSA key <see cref="string" />.</param>
		/// <returns>
		/// The equivalent PEM key, represented as a <see cref="string" />.
		/// </returns>
		public static string ToPem(RSAParameters key, AsymmetricKeyType keyType)
		{
			return ToPem(ToDer(key, keyType), keyType);
		}
		/// <summary>
		/// Converts a public or private RSA key, represented in DER format to its equivalent <see cref="RSAParameters" /> structure.
		/// </summary>
		/// <param name="derKey">A <see cref="byte" />[] with the DER key in its binary representation.</param>
		/// <returns>
		/// A new <see cref="RSAParameters" /> structure with the converted key.
		/// </returns>
		public static RSAParameters ToKey(byte[] derKey)
		{
			Check.ArgumentNull(derKey, nameof(derKey));

			if (ImportPrivateKey(out RSAParameters privateKey)) return privateKey;
			else if (ImportPublicKey(out RSAParameters publicKey)) return publicKey;
			else throw CreateFormatException();

			bool ImportPublicKey(out RSAParameters key)
			{
				key = new RSAParameters();

				using (BinaryReader reader = new BinaryReader(new MemoryStream(derKey)))
				{
					if (!ReadDataSequence(reader)) return false;

					if (!reader.ReadBytes(15).Compare(new byte[] { 0x30, 0x0d, 0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01, 0x05, 0x00 }))
					{
						return false;
					}

					switch (reader.ReadUInt16())
					{
						case 0x8103: reader.ReadByte(); break;
						case 0x8203: reader.ReadInt16(); break;
						default: return false;
					}

					if (reader.ReadByte() != 0) return false;
					if (!ReadDataSequence(reader)) return false;

					int moduluSize;
					switch (reader.ReadUInt16())
					{
						case 0x8102: moduluSize = reader.ReadByte(); break;
						case 0x8202: moduluSize = reader.ReadByte() << 8 | reader.ReadByte(); break;
						default: return false;
					}

					byte firstByte = reader.ReadByte();
					reader.BaseStream.Seek(-1, SeekOrigin.Current);

					if (firstByte == 0)
					{
						reader.ReadByte();
						moduluSize -= 1;
					}

					byte[] modulus = reader.ReadBytes(moduluSize);
					if (reader.ReadByte() != 2) return false;

					key = new RSAParameters
					{
						Modulus = modulus,
						Exponent = reader.ReadBytes(reader.ReadByte())
					};

					return true;
				}
			}
			bool ImportPrivateKey(out RSAParameters key)
			{
				key = new RSAParameters();

				using (BinaryReader reader = new BinaryReader(new MemoryStream(derKey)))
				{
					if (!ReadDataSequence(reader)) return false;
					if (reader.ReadUInt16() != 0x102 || reader.ReadByte() != 0) return false;

					key = new RSAParameters
					{
						Modulus = ReadParameter(),
						Exponent = ReadParameter(),
						D = ReadParameter(),
						P = ReadParameter(),
						Q = ReadParameter(),
						DP = ReadParameter(),
						DQ = ReadParameter(),
						InverseQ = ReadParameter()
					};
					return true;

					byte[] ReadParameter()
					{
						if (reader.ReadByte() != 2) return new byte[0];

						int size = reader.ReadByte();

						if (size == 0x81) size = reader.ReadByte();
						else if (size == 0x82) size = reader.ReadByte() << 8 | reader.ReadByte();

						while (reader.ReadByte() == 0) size--;
						reader.BaseStream.Seek(-1, SeekOrigin.Current);

						return reader.ReadBytes(size);
					}
				}
			}
			bool ReadDataSequence(BinaryReader reader)
			{
				switch (reader.ReadUInt16())
				{
					case 0x8130: reader.ReadByte(); break;
					case 0x8230: reader.ReadInt16(); break;
					default: return false;
				}

				return true;
			}
		}
		/// <summary>
		/// Converts a public or private RSA key, represented in PEM format to its equivalent <see cref="RSAParameters" /> structure.
		/// </summary>
		/// <param name="pemKey">A <see cref="string" /> value with the PEM key, starting with "-----BEGIN PUBLIC KEY-----" or "-----BEGIN RSA PRIVATE KEY-----".</param>
		/// <returns>
		/// A new <see cref="RSAParameters" /> structure with the converted key.
		/// </returns>
		public static RSAParameters ToKey(string pemKey)
		{
			Check.ArgumentNull(pemKey, nameof(pemKey));

			return ToKey(ToDer(pemKey));
		}

		private static Exception CreateFormatException()
		{
			return Throw.Format("The RSA key format is incorrect.");
		}
	}
}