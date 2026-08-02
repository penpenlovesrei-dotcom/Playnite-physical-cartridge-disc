using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;

using CartridgeShelf.Core;

using Playnite.SDK;

namespace CartridgeShelf.Detectors
{
    /// <summary>
    /// Détecteur SN Operator (Epilogue), via port série virtuel (CDC-ACM).
    ///
    /// Les interfaces 0+1 du device composite forment une paire USB
    /// CDC-ACM standard -- un simple port série virtuel, entièrement géré
    /// par le driver Windows natif (usbser.sys). Pas besoin de
    /// WinUSB/Zadig pour y accéder. C'est aussi ce que Playback utilise
    /// lui-même (cohérent avec Qt6SerialPort.dll vu dans son dossier
    /// d'installation).
    ///
    /// IMPORTANT -- partage du port avec Playback : un port COM ne peut
    /// être ouvert que par un seul programme à la fois (contrainte
    /// Windows, pas contournable). Pour permettre à Playback et
    /// CartridgeShelf de tourner en même temps sans que l'un bloque
    /// l'autre en continu, ce détecteur ouvre le port, envoie la
    /// commande, lit la réponse, PUIS LE REFERME immédiatement à chaque
    /// cycle -- au lieu de le garder ouvert en permanence. Ça laisse le
    /// port libre la quasi-totalité du temps entre deux polls (~1s), avec
    /// juste une petite fenêtre de conflit possible à chaque cycle. Un
    /// cycle raté (port occupé par Playback à ce moment précis) retourne
    /// simplement null (état indéterminé, cf. CartridgeWatcher) -- jamais
    /// interprété comme un retrait de cartouche.
    ///
    /// Protocole (voir historique du projet) :
    ///   - Commande "get cartridge info" : 64 octets = [0x04] + 59x0x00
    ///     + CRC32/MPEG-2 (little-endian) sur les 60 premiers octets
    ///   - Réponse : chercher un enregistrement commençant par 0x01 0x01
    ///     dans le flux reçu (pas de délimitation par paquet sur un port
    ///     série, contrairement à l'USB brut -- il faut accumuler les
    ///     octets reçus et scanner la signature dedans)
    ///   - byte[2]==0x50 si cartouche présente, checksum 16 bits en
    ///     byte[13:15] (little-endian), taille en byte[5], flag SRAM en
    ///     byte[6:8]==0x0D21
    /// </summary>
    public class SnOperatorDetector : ICartridgeDetector, IDisposable
    {
        private const int VendorId = 0x16D0;
        private const int ProductId = 0x123E;
        private const byte CmdGetCartridgeInfo = 0x04;
        private const int BaudRate = 115200;
        private const int ReadTimeoutMs = 800;
        private const int MaxBufferBytes = 4096;

        private readonly ILogger logger;

        private readonly List<byte> receiveBuffer = new List<byte>();

        public PlatformId Platform => PlatformId.SuperNintendo;


        public SnOperatorDetector()
        {
            logger = LogManager.GetLogger();
        }


        /// <summary>
        /// Vérification légère : le device est-il présent sur le système
        /// (port COM énuméré) ? N'ouvre PAS le port -- pas besoin de le
        /// monopoliser juste pour vérifier sa présence.
        /// </summary>
        public bool CanHandle()
        {
            return FindComPort(VendorId, ProductId) != null;
        }


        /// <summary>
        /// Ouvre le port, envoie la commande "get cartridge info", lit la
        /// réponse, puis referme le port -- tout de suite, à chaque appel
        /// (voir note de classe sur le partage du port avec Playback).
        /// Retourne l'état décodé, ou null si aucun enregistrement valide
        /// n'a pu être lu ce cycle (port occupé par Playback, device
        /// débranché, etc.) : état indéterminé, PAS une absence de
        /// cartouche.
        /// </summary>
        public CartridgeInfo Read()
        {
            string portName = FindComPort(VendorId, ProductId);

            if (portName == null)
            {
                return null;
            }

            System.IO.Ports.SerialPort port = null;

            try
            {
                port = new System.IO.Ports.SerialPort(portName, BaudRate)
                {
                    ReadTimeout = ReadTimeoutMs,
                    WriteTimeout = ReadTimeoutMs,
                    DtrEnable = true,
                    RtsEnable = true
                };

                port.Open();

                receiveBuffer.Clear();

                byte[] command = BuildCommand(CmdGetCartridgeInfo);

                port.Write(command, 0, command.Length);

                return ReadRecordFromStream(port);
            }
            catch (UnauthorizedAccessException)
            {
                // Port ouvert par un autre programme (typiquement Playback)
                // au moment précis de ce cycle -- pas une erreur en soi.
                return null;
            }
            catch (Exception ex)
            {
                logger.Info($"SnOperatorDetector : erreur de communication série ({ex.Message}).");

                return null;
            }
            finally
            {
                try
                {
                    if (port != null && port.IsOpen)
                    {
                        port.Close();
                    }

                    port?.Dispose();
                }
                catch { /* ignoré */ }
            }
        }


        public GameIdentity ToIdentity(CartridgeInfo info)
        {
            return new GameIdentity
            {
                Platform = Platform,
                Checksum = info.Checksum.ToString("x4")
            };
        }


        public void Dispose()
        {
            // Rien à libérer : le port n'est jamais gardé ouvert entre
            // deux appels à Read() (voir note de classe).
        }


        /// <summary>
        /// Trouve le port COM associé à ce VID/PID via WMI. Nécessaire
        /// car, contrairement à un accès USB direct par VID/PID, un port
        /// série s'ouvre par son nom ("COM4"...), qui peut changer d'une
        /// machine ou d'un rebranchement à l'autre.
        /// </summary>
        private static string FindComPort(int vendorId, int productId)
        {
            string vidPid = $"VID_{vendorId:X4}&PID_{productId:X4}";

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'"))
            {
                foreach (ManagementObject device in searcher.Get())
                {
                    string deviceId = device["DeviceID"] as string ?? string.Empty;

                    if (deviceId.IndexOf(vidPid, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string caption = device["Caption"] as string ?? string.Empty;

                    Match match = Regex.Match(caption, @"\(COM(\d+)\)");

                    if (match.Success)
                    {
                        return "COM" + match.Groups[1].Value;
                    }
                }
            }

            return null;
        }


        /// <summary>
        /// Accumule les octets reçus sur le port série (flux continu, pas
        /// de découpage en paquets comme en USB brut) jusqu'à trouver un
        /// enregistrement valide (signature 0x01 0x01 + assez d'octets pour
        /// lire le checksum), ou jusqu'à expiration du délai imparti.
        /// </summary>
        private CartridgeInfo ReadRecordFromStream(System.IO.Ports.SerialPort port)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(ReadTimeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                int available = port.BytesToRead;

                if (available > 0)
                {
                    byte[] chunk = new byte[available];

                    port.Read(chunk, 0, available);

                    receiveBuffer.AddRange(chunk);

                    for (int i = 0; i <= receiveBuffer.Count - 15; i++)
                    {
                        if (receiveBuffer[i] == 0x01 && receiveBuffer[i + 1] == 0x01)
                        {
                            int recordLength = Math.Min(24, receiveBuffer.Count - i);
                            byte[] record = receiveBuffer.GetRange(i, recordLength).ToArray();

                            receiveBuffer.RemoveRange(0, i + recordLength);

                            return ParseRecord(record, record.Length);
                        }
                    }

                    if (receiveBuffer.Count > MaxBufferBytes)
                    {
                        receiveBuffer.Clear();
                    }
                }
                else
                {
                    Thread.Sleep(20);
                }
            }

            return null;
        }


        private static CartridgeInfo ParseRecord(byte[] record, int length)
        {
            CartridgeInfo info = new CartridgeInfo
            {
                Present = length > 2 && record[2] == 0x50
            };

            if (info.Present && length >= 15)
            {
                info.Checksum = (ushort)(record[13] | (record[14] << 8));
                info.SizeCode = record[5];
                info.HasSram = length > 7 && record[6] == 0x0D && record[7] == 0x21;
            }

            return info;
        }


        private static byte[] BuildCommand(byte cmdByte)
        {
            byte[] payload = new byte[60];
            payload[0] = cmdByte;

            uint crc = Crc32Mpeg2(payload);

            byte[] result = new byte[64];
            Array.Copy(payload, result, 60);
            result[60] = (byte)(crc & 0xFF);
            result[61] = (byte)((crc >> 8) & 0xFF);
            result[62] = (byte)((crc >> 16) & 0xFF);
            result[63] = (byte)((crc >> 24) & 0xFF);

            return result;
        }


        /// <summary>
        /// CRC32/MPEG-2 : polynôme 0x04C11DB7, init 0xFFFFFFFF, pas de
        /// reflet, pas de XOR final.
        /// </summary>
        private static uint Crc32Mpeg2(byte[] data)
        {
            uint crc = 0xFFFFFFFF;

            foreach (byte b in data)
            {
                crc ^= (uint)b << 24;

                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x80000000) != 0
                        ? (crc << 1) ^ 0x04C11DB7
                        : crc << 1;
                }
            }

            return crc;
        }
    }
}
