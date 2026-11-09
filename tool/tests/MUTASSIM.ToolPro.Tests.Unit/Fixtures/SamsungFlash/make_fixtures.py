"""Generates the Samsung flash test fixtures (run once; outputs are committed).
Requires: pip install lz4 ; optional `lz4` CLI for the legacy-format sample."""
import hashlib, io, os, random, shutil, struct, subprocess, tarfile
import lz4.frame as f

out = os.path.dirname(os.path.abspath(__file__))
rnd = random.Random(42)

def payload(n):
    # compressible but non-trivial data with long-distance repeats (exercises dependent blocks)
    words = [bytes(rnd.getrandbits(8) for _ in range(rnd.randint(3, 40))) for _ in range(300)]
    b = bytearray()
    while len(b) < n:
        b += rnd.choice(words)
        if rnd.random() < 0.05: b += bytes(rnd.getrandbits(8) for _ in range(200))
    return bytes(b[:n])

data = payload(700_000)
open(os.path.join(out, "sample.bin"), "wb").write(data)
variants = {
    "linked_64k_cc": dict(block_size=f.BLOCKSIZE_MAX64KB, block_linked=True, content_checksum=True, store_size=True),
    "indep_256k_bc": dict(block_size=f.BLOCKSIZE_MAX256KB, block_linked=False, block_checksum=True),
    "linked_1m_all": dict(block_size=f.BLOCKSIZE_MAX1MB, block_linked=True, block_checksum=True, content_checksum=True, store_size=True),
    "indep_4m_none": dict(block_size=f.BLOCKSIZE_MAX4MB, block_linked=False, content_checksum=False, store_size=False),
}
for name, kw in variants.items():
    open(os.path.join(out, f"sample.{name}.lz4"), "wb").write(f.compress(data, **kw))
# two concatenated frames + an uncompressible block
noise = bytes(rnd.getrandbits(8) for _ in range(100_000))
open(os.path.join(out, "multi.bin"), "wb").write(data[:200_000] + noise)
open(os.path.join(out, "multi.lz4"), "wb").write(
    f.compress(data[:200_000], block_linked=True, content_checksum=True) + f.compress(noise, content_checksum=True))
if shutil.which("lz4"):
    subprocess.run(["lz4", "-l", "-f", "-q", os.path.join(out, "sample.bin"), os.path.join(out, "sample.legacy.lz4")], check=True)

# ---- PIT -----------------------------------------------------------------
PARTS = [("BOOTLOADER", "sboot.bin"), ("PARAM", "param.bin"), ("BOOT", "boot.img"), ("SUPER", "super.img"),
         ("RADIO", "modem.bin"), ("CACHE", "cache.img"), ("OPTICS", "optics.img"), ("PERSISTENT", "persistent.img"),
         ("EFS", "efs.img"), ("USERDATA", "userdata.img"), ("PIT", "")]
def pit(parts):
    b = struct.pack("<II8s8sHH", 0x12349876, len(parts), b"COM_TAR2", b"EXYNOS9", 1, 0)
    for i, (name, fn) in enumerate(parts):
        b += struct.pack("<9I32s32s32s", 2, 8, 70 + i, 5, 1, 0, 1024, 0, 0, name.encode(), fn.encode(), b"")
    return b
open(os.path.join(out, "device.pit"), "wb").write(pit(PARTS))
open(os.path.join(out, "device_other.pit"), "wb").write(pit([p for p in PARTS if p[0] != "OPTICS"]))

# ---- firmware packages (.tar.md5) -----------------------------------------
images = {name: payload(rnd.randint(20_000, 90_000)) for _, name in PARTS if name}
open(os.path.join(out, "images.md5"), "w").write(
    "\n".join(f"{n} {hashlib.md5(d).hexdigest()}" for n, d in images.items()))

def tarmd5(fname, members):
    bio = io.BytesIO()
    with tarfile.open(fileobj=bio, mode="w", format=tarfile.USTAR_FORMAT) as t:
        for name, content in members:
            ti = tarfile.TarInfo(name); ti.size = len(content); ti.mode = 0o644
            t.addfile(ti, io.BytesIO(content))
    raw = bio.getvalue()
    md5 = hashlib.md5(raw).hexdigest()
    open(os.path.join(out, fname), "wb").write(raw + f"{md5}  {fname[:-4]}\n".encode())

z = lambda n: (n + ".lz4", f.compress(images[n], block_linked=True, content_checksum=True, store_size=True))
B = "A515FXXU8HWE1"
C = "A515FOXM8HWE1"  # CSC build (real files use the CSC code in the build)
tarmd5(f"BL_{B}_CL1_user_low_ship.tar.md5", [z("sboot.bin"), ("param.bin", images["param.bin"])])
tarmd5(f"AP_{B}_CL1_user_low_ship_meta_OS13.tar.md5", [z("boot.img"), z("super.img"), ("meta-data/fota.zip", b"PK\x05\x06" + bytes(18))])
tarmd5(f"CP_{B}_CP1_CL1_user_low_ship.tar.md5", [z("modem.bin")])
tarmd5(f"HOME_CSC_OXM_{C}_CL1_user_low_ship.tar.md5", [z("cache.img"), z("optics.img"), ("A51_EUR_OPEN.pit", pit(PARTS))])
tarmd5(f"CSC_OXM_{C}_CL1_user_low_ship.tar.md5", [z("cache.img"), z("optics.img"), ("A51_EUR_OPEN.pit", pit(PARTS))])
tarmd5(f"AP_{B}_TAMPERED.tar.md5", [z("boot.img"), z("persistent.img"), ("efs.img", images["efs.img"])])
tarmd5("AP_G991BXXU5DWE1_OTHER_MODEL.tar.md5", [z("boot.img")])
tarmd5(f"USERDATA_{B}_CL1_user_low_ship.tar.md5", [z("userdata.img")])
tarmd5(f"AP_{B}_FACTORY.tar.md5", [z("boot.img"), z("super.img"), z("userdata.img")])
# damaged: valid tar, wrong embedded md5
bad = open(os.path.join(out, f"CP_{B}_CP1_CL1_user_low_ship.tar.md5"), "rb").read()
open(os.path.join(out, f"CP_{B}_DAMAGED.tar.md5"), "wb").write(bad[:2000] + bytes([bad[2000] ^ 0xFF]) + bad[2001:])
print("fixtures written")
