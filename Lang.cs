using System.Collections.Generic;

namespace DonatScreen
{
    /// <summary>
    /// UI text. The app was written in Indonesian: those strings are the keys, <see cref="En"/> holds the English.
    /// English is the default; <c>Lang=id</c> in the ini switches to Indonesian (live, from Settings).
    /// test.ps1 checks that every L.T("…") key has an entry here with the same {n} slots.
    /// </summary>
    static class L
    {
        public static bool Indonesian;

        public static string T(string id, params object[] args)
        {
            var s = !Indonesian && En.TryGetValue(id, out var en) ? en : id;
            return args.Length == 0 ? s : string.Format(s, args);
        }

        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            // tray
            ["Ambil area"] = "Capture area",
            ["Ambil layar penuh"] = "Capture full screen",
            ["Buka folder default"] = "Open default folder",
            ["Pengaturan"] = "Settings",
            ["Keluar"] = "Quit",
            ["Donat Screen aktif"] = "Donat Screen is running",
            ["Area: {0}   ·   Layar penuh: {1}"] = "Area: {0}   ·   Full screen: {1}",
            ["Shortcut {0} bentrok dengan aplikasi lain. Ganti di Pengaturan."] = "Shortcut {0} is taken by another app. Change it in Settings.",
            ["Shortcut tidak aktif"] = "Shortcut not active",
            ["Screenshot gagal"] = "Screenshot failed",
            ["Disalin ke clipboard"] = "Copied to the clipboard",
            ["Clipboard sedang dipakai aplikasi lain, tidak disalin"] = "The clipboard is busy in another app, not copied",
            ["Tersimpan di {0}"] = "Saved to {0}",
            ["Sebagian gagal disimpan"] = "Some copies could not be saved",
            ["Gagal menyimpan file"] = "Could not save the file",
            ["Gambar tetap ada di clipboard."] = "The image is still on the clipboard.",
            ["Folder tidak bisa dibuka"] = "The folder can't be opened",
            // shortcut rules
            ["PrtSc dipakai Snipping Tool. Matikan di Pengaturan Windows › Aksesibilitas › Keyboard, atau pilih kombinasi lain."] =
                "PrtSc is used by the Snipping Tool. Turn that off in Windows Settings › Accessibility › Keyboard, or pick another combination.",
            ["{0} adalah shortcut bawaan Windows."] = "{0} is a built-in Windows shortcut.",
            ["Win + satu tombol dipakai Windows. Tambahkan Ctrl, Alt, atau Shift."] = "Win + one key is used by Windows. Add Ctrl, Alt or Shift.",
            ["Pakai minimal dua dari Ctrl, Alt, Shift (atau Win) agar tidak bentrok dengan shortcut aplikasi."] =
                "Use at least two of Ctrl, Alt and Shift (or Win), so it doesn't clash with app shortcuts.",
            // settings window
            ["Klik kotak shortcut, lalu tekan kombinasi baru. Bisa pakai Ctrl, Alt, Shift, dan Win."] =
                "Click a shortcut box, then press the new combination. Ctrl, Alt, Shift and Win all work.",
            ["Tidak bisa menulis DonatScreen.ini di folder aplikasi."] = "Can't write DonatScreen.ini in the app folder.",
            ["Folder itu sudah ada di daftar."] = "That folder is already in the list.",
            ["Folder tujuan"] = "Save folders",
            ["Tambah folder"] = "Add folder",
            ["Centang = tampil di popup  ·  ★ = default"] = "Ticked = offered in the popup  ·  ★ = default",
            ["Setelah screenshot"] = "After a screenshot",
            ["Tanya folder dan nama file"] = "Ask for folder and file name",
            ["Jika mati, langsung disimpan ke folder ★ dengan nama otomatis."] = "When off, saves straight to the ★ folder with an automatic name.",
            ["Shortcut"] = "Shortcuts",
            ["Umum"] = "General",
            ["Jalankan saat Windows menyala"] = "Start with Windows",
            ["Folder default selalu muncul di pilihan."] = "The default folder is always offered.",
            ["Klik path untuk mengubah lokasi"] = "Click the path to change the location",
            ["Buka folder"] = "Open folder",
            ["Folder default"] = "Default folder",
            ["Jadikan default"] = "Make default",
            ["Hapus dari daftar (isi folder tidak dihapus)"] = "Remove from the list (the folder's contents stay)",
            ["Folder tidak ditemukan: {0}"] = "Folder not found: {0}",
            ["{0} sudah dipakai untuk {1}."] = "{0} is already used for {1}.",
            ["{0} sudah dipakai Windows atau aplikasi lain."] = "{0} is already used by Windows or another app.",
            ["Autostart tidak bisa diubah: {0}"] = "Autostart can't be changed: {0}",
            ["Tekan shortcut…"] = "Press a shortcut…",
            // save dialog
            ["Simpan screenshot"] = "Save screenshot",
            ["Nama file"] = "File name",
            ["Simpan ke  —  centang satu atau beberapa folder"] = "Save to  —  tick one or more folders",
            ["Enter simpan   ·   Esc simpan ke ★"] = "Enter save   ·   Esc save to ★",
            ["Simpan"] = "Save",
            ["Nama tidak boleh memuat \\ / : * ? \" < > | atau nama khusus Windows (CON, NUL, …)."] =
                "A name can't contain \\ / : * ? \" < > | or a reserved Windows name (CON, NUL, …).",
            ["Centang minimal satu folder tujuan."] = "Tick at least one folder.",
            // region overlay
            ["Seret untuk memilih area   ·   Klik untuk satu layar   ·   Esc batal"] = "Drag to select an area   ·   Click for one screen   ·   Esc to cancel",
        };
    }
}
