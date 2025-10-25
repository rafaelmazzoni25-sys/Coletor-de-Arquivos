#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Coletor de arquivos por extensão (ex.: .rar) com interface gráfica (Tkinter).
Fase 1: Busca e listagem de todos os arquivos correspondentes.
Fase 2: Seleção de quais arquivos serão copiados e execução da cópia.

Principais recursos:
- Selecionar múltiplas raízes (unidades/pastas) para busca
- Definir pasta de destino
- Escolher extensões (separadas por vírgula ou espaço)
- Opções: sobrescrever, dry-run (simulação), seguir symlinks
- Busca assíncrona (thread), com cancelar
- Visualização dos resultados em Treeview com multiseleção
- Ações: Copiar selecionados, Copiar todos, Selecionar tudo, Limpar, Inverter seleção
- Log detalhado e progresso

Requisitos: Python 3.x (tkinter já vem incluso na instalação oficial do Python no Windows)
"""

import os
import sys
import shutil
import threading
import queue
from datetime import datetime
from pathlib import Path
from typing import List, Iterable, Tuple

import tkinter as tk
from tkinter import ttk, filedialog, messagebox

APP_TITLE = "Coletor de Arquivos (seleção antes da cópia)"

# -------------------------- LÓGICA DE ARQUIVOS --------------------------

def normalize_roots(roots: Iterable[str]) -> List[Path]:
    out = []
    for r in roots:
        p = Path(r).expanduser().resolve()
        if not p.exists():
            raise FileNotFoundError(f"Root '{r}' não existe.")
        out.append(p)
    return out


def parse_exts(text: str) -> List[str]:
    # Divide por vírgula/ espaço; normaliza com ponto e lowercase
    raw = [t.strip() for t in text.replace(",", " ").split() if t.strip()]
    exts = []
    for e in raw:
        if not e:
            continue
        if not e.startswith('.'):  # aceita "rar" e vira ".rar"
            e = '.' + e
        exts.append(e.lower())
    return exts


def matches_ext(filename: str, exts: List[str]) -> bool:
    name = filename.lower()
    return any(name.endswith(e) for e in exts)


def copy_with_structure(src_path: Path, root: Path, dest_root: Path, overwrite: bool=False, dry_run: bool=False) -> str:
    try:
        rel = src_path.relative_to(root)
    except Exception:
        # fallback: relativo à âncora do disco
        rel = src_path.relative_to(src_path.anchor)

    dest_path = dest_root.joinpath(rel)
    dest_dir = dest_path.parent

    if dry_run:
        return f"[DRY-RUN] {src_path} -> {dest_path}"

    dest_dir.mkdir(parents=True, exist_ok=True)
    if dest_path.exists() and not overwrite:
        return f"[PULADO - existe] {dest_path}"

    shutil.copy2(src_path, dest_path)
    return f"[OK] {src_path} -> {dest_path}"


# Resultado de busca: (src_path, root, size_bytes, mtime_ts)
SearchHit = Tuple[Path, Path, int, float]

def find_files(roots: List[Path], exts: List[str], follow_symlinks: bool=False,
               stop_event: threading.Event=None, log_q: queue.Queue=None, hit_q: queue.Queue=None):
    files_count = 0

    for root in roots:
        if stop_event and stop_event.is_set():
            break
        if log_q:
            log_q.put(f"Procurando em: {root}")
        for dirpath, dirnames, filenames in os.walk(root, followlinks=follow_symlinks):
            if stop_event and stop_event.is_set():
                break
            dirpath_p = Path(dirpath)
            try:
                for fn in filenames:
                    if stop_event and stop_event.is_set():
                        break
                    if matches_ext(fn, exts):
                        files_count += 1
                        src = dirpath_p.joinpath(fn)
                        try:
                            st = src.stat()
                            hit: SearchHit = (src, root, int(st.st_size), float(st.st_mtime))
                            if hit_q:
                                hit_q.put(hit)
                        except PermissionError:
                            if log_q:
                                log_q.put(f"[ERRO - permissão] {src}")
                        except OSError as e:
                            if log_q:
                                log_q.put(f"[ERRO - OS] {src}: {e}")
            except PermissionError:
                if log_q:
                    log_q.put(f"[ERRO - permissão] ao ler pasta {dirpath}")
            except OSError as e:
                if log_q:
                    log_q.put(f"[ERRO - OS] ao acessar {dirpath}: {e}")

    if log_q:
        log_q.put(f"Busca finalizada. Encontrados: {files_count}")


# -------------------------- INTERFACE (TKINTER) --------------------------

class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title(APP_TITLE)
        self.geometry("1100x650")

        self.log_q: queue.Queue[str] = queue.Queue()
        self.hit_q: queue.Queue[SearchHit] = queue.Queue()
        self.worker_thread = None
        self.copy_thread = None
        self.stop_event = threading.Event()

        self.found_hits: List[SearchHit] = []

        self._build_ui()
        self._poll_queues()

    # UI
    def _build_ui(self):
        # ====== Roots frame ======
        frm_roots = ttk.LabelFrame(self, text="Raízes para busca (unidades/pastas)")
        frm_roots.pack(fill=tk.X, padx=10, pady=8)

        self.lst_roots = tk.Listbox(frm_roots, height=4, selectmode=tk.EXTENDED)
        self.lst_roots.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(10, 6), pady=8)

        root_btns = ttk.Frame(frm_roots)
        root_btns.pack(side=tk.LEFT, fill=tk.Y, padx=6, pady=8)
        ttk.Button(root_btns, text="Adicionar pasta...", command=self.add_root_folder).pack(fill=tk.X, pady=2)
        ttk.Button(root_btns, text="Detectar unidades", command=self.detect_drives).pack(fill=tk.X, pady=2)
        ttk.Button(root_btns, text="Adicionar unidade...", command=self.add_drive_manual).pack(fill=tk.X, pady=2)
        ttk.Button(root_btns, text="Remover selecionadas", command=self.remove_selected_roots).pack(fill=tk.X, pady=2)

        # ====== Destino ======
        frm_dest = ttk.LabelFrame(self, text="Pasta de destino (onde será replicada a estrutura)")
        frm_dest.pack(fill=tk.X, padx=10, pady=8)
        self.var_dest = tk.StringVar()
        ent_dest = ttk.Entry(frm_dest, textvariable=self.var_dest)
        ent_dest.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(10, 6), pady=8)
        ttk.Button(frm_dest, text="Escolher...", command=self.choose_dest).pack(side=tk.LEFT, padx=(0,10))

        # ====== Opções ======
        frm_opts = ttk.LabelFrame(self, text="Opções")
        frm_opts.pack(fill=tk.X, padx=10, pady=8)

        self.var_exts = tk.StringVar(value=".rar")
        ttk.Label(frm_opts, text="Extensões (vírgula ou espaço):").pack(side=tk.LEFT, padx=(10,6), pady=8)
        ttk.Entry(frm_opts, width=40, textvariable=self.var_exts).pack(side=tk.LEFT, padx=(0,12))

        self.var_overwrite = tk.BooleanVar(value=False)
        self.var_symlinks = tk.BooleanVar(value=False)
        self.var_dryrun = tk.BooleanVar(value=False)
        ttk.Checkbutton(frm_opts, text="Sobrescrever se existir", variable=self.var_overwrite).pack(side=tk.LEFT, padx=6)
        ttk.Checkbutton(frm_opts, text="Seguir symlinks", variable=self.var_symlinks).pack(side=tk.LEFT, padx=6)
        ttk.Checkbutton(frm_opts, text="Simular (dry-run)", variable=self.var_dryrun).pack(side=tk.LEFT, padx=6)

        # ====== Ações ======
        frm_actions = ttk.Frame(self)
        frm_actions.pack(fill=tk.X, padx=10, pady=(0,8))
        self.btn_search = ttk.Button(frm_actions, text="Buscar", command=self.start_search)
        self.btn_search.pack(side=tk.LEFT, padx=(10,6))
        self.btn_cancel = ttk.Button(frm_actions, text="Cancelar", command=self.cancel, state=tk.DISABLED)
        self.btn_cancel.pack(side=tk.LEFT, padx=6)
        self.btn_open_dest = ttk.Button(frm_actions, text="Abrir destino", command=self.open_dest)
        self.btn_open_dest.pack(side=tk.LEFT, padx=6)

        # ====== Progresso ======
        frm_prog = ttk.Frame(self)
        frm_prog.pack(fill=tk.X, padx=10, pady=4)
        ttk.Label(frm_prog, text="Progresso:").pack(side=tk.LEFT, padx=(10,6))
        self.pb = ttk.Progressbar(frm_prog, mode='indeterminate')
        self.pb.pack(fill=tk.X, expand=True, padx=(0,10))
        self.var_status = tk.StringVar(value="Pronto.")
        ttk.Label(frm_prog, textvariable=self.var_status).pack(side=tk.LEFT, padx=6)

        # ====== Resultados ======
        frm_results = ttk.LabelFrame(self, text="Resultados da busca (selecione os que deseja copiar)")
        frm_results.pack(fill=tk.BOTH, expand=True, padx=10, pady=(4,10))

        cols = ("nome", "raiz", "relpath", "tam_mb", "modificado")
        self.tree = ttk.Treeview(frm_results, columns=cols, show='headings', selectmode='extended')
        self.tree.heading("nome", text="Nome")
        self.tree.heading("raiz", text="Raiz")
        self.tree.heading("relpath", text="Caminho relativo")
        self.tree.heading("tam_mb", text="Tamanho (MB)")
        self.tree.heading("modificado", text="Modificado")
        self.tree.column("nome", width=260)
        self.tree.column("raiz", width=120)
        self.tree.column("relpath", width=460)
        self.tree.column("tam_mb", width=110, anchor=tk.E)
        self.tree.column("modificado", width=160, anchor=tk.CENTER)
        self.tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(10,0), pady=8)

        res_scroll_y = ttk.Scrollbar(frm_results, orient=tk.VERTICAL, command=self.tree.yview)
        res_scroll_y.pack(side=tk.LEFT, fill=tk.Y, pady=8)
        self.tree.configure(yscrollcommand=res_scroll_y.set)

        res_scroll_x = ttk.Scrollbar(frm_results, orient=tk.HORIZONTAL, command=self.tree.xview)
        res_scroll_x.pack(side=tk.BOTTOM, fill=tk.X, padx=(10,10))
        self.tree.configure(xscrollcommand=res_scroll_x.set)

        # ====== Botões de seleção/cópia ======
        frm_sel = ttk.Frame(self)
        frm_sel.pack(fill=tk.X, padx=10, pady=(0,8))
        ttk.Button(frm_sel, text="Selecionar tudo", command=self.select_all).pack(side=tk.LEFT, padx=6)
        ttk.Button(frm_sel, text="Limpar seleção", command=self.clear_selection).pack(side=tk.LEFT, padx=6)
        ttk.Button(frm_sel, text="Inverter seleção", command=self.invert_selection).pack(side=tk.LEFT, padx=6)
        self.btn_copy_sel = ttk.Button(frm_sel, text="Copiar selecionados", command=self.copy_selected, state=tk.DISABLED)
        self.btn_copy_sel.pack(side=tk.LEFT, padx=12)
        self.btn_copy_all = ttk.Button(frm_sel, text="Copiar todos", command=self.copy_all, state=tk.DISABLED)
        self.btn_copy_all.pack(side=tk.LEFT, padx=6)

        # ====== Log ======
        frm_log = ttk.LabelFrame(self, text="Log")
        frm_log.pack(fill=tk.BOTH, expand=True, padx=10, pady=(0,10))

        self.txt_log = tk.Text(frm_log, height=10, wrap=tk.NONE)
        self.txt_log.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(10,0), pady=8)

        log_scroll_y = ttk.Scrollbar(frm_log, orient=tk.VERTICAL, command=self.txt_log.yview)
        log_scroll_y.pack(side=tk.LEFT, fill=tk.Y, pady=8)
        self.txt_log.configure(yscrollcommand=log_scroll_y.set)

        log_scroll_x = ttk.Scrollbar(frm_log, orient=tk.HORIZONTAL, command=self.txt_log.xview)
        log_scroll_x.pack(side=tk.BOTTOM, fill=tk.X, padx=(10,10))
        self.txt_log.configure(xscrollcommand=log_scroll_x.set)

    # ----- Helpers UI -----
    def add_root_folder(self):
        folder = filedialog.askdirectory(title="Escolha uma pasta raiz")
        if folder:
            self.lst_roots.insert(tk.END, folder)

    def detect_drives(self):
        # Apenas Windows: varre de A: a Z:
        added = 0
        for d in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
            drive = f"{d}:\\"
            try:
                if Path(drive).exists():
                    self.lst_roots.insert(tk.END, drive)
                    added += 1
            except Exception:
                pass
        if added == 0:
            messagebox.showinfo(APP_TITLE, "Nenhuma unidade detectada.")

    def add_drive_manual(self):
        # Caixa simples para digitar letra de unidade
        win = tk.Toplevel(self)
        win.title("Adicionar unidade")
        win.resizable(False, False)
        ttk.Label(win, text="Letra da unidade (ex.: C):").pack(padx=10, pady=(10,4))
        var = tk.StringVar()
        ent = ttk.Entry(win, textvariable=var, width=5)
        ent.pack(padx=10, pady=4)
        ent.focus_set()
        def ok():
            v = var.get().strip().upper().rstrip(':\\\\/')
            if v:
                drive = f"{v}:\\"
                if Path(drive).exists():
                    self.lst_roots.insert(tk.END, drive)
                else:
                    messagebox.showerror(APP_TITLE, f"Unidade '{drive}' não existe.")
            win.destroy()
        ttk.Button(win, text="OK", command=ok).pack(padx=10, pady=(4,10))

    def remove_selected_roots(self):
        for idx in reversed(self.lst_roots.curselection()):
            self.lst_roots.delete(idx)

    def choose_dest(self):
        folder = filedialog.askdirectory(title="Escolha a pasta de destino")
        if folder:
            self.var_dest.set(folder)

    def open_dest(self):
        dest = self.var_dest.get().strip()
        if not dest:
            messagebox.showinfo(APP_TITLE, "Defina a pasta de destino primeiro.")
            return
        p = Path(dest)
        try:
            if sys.platform.startswith('win'):
                os.startfile(str(p))  # type: ignore[attr-defined]
            elif sys.platform == 'darwin':
                os.system(f"open '{p}'")
            else:
                os.system(f"xdg-open '{p}'")
        except Exception as e:
            messagebox.showerror(APP_TITLE, f"Não foi possível abrir: {e}")

    # ----- Execução: Busca -----
    def start_search(self):
        if self.worker_thread and self.worker_thread.is_alive():
            messagebox.showwarning(APP_TITLE, "Já existe uma busca em andamento.")
            return
        roots = list(self.lst_roots.get(0, tk.END))
        if not roots:
            messagebox.showerror(APP_TITLE, "Adicione ao menos uma raiz (unidade ou pasta).")
            return
        dest = self.var_dest.get().strip()
        if not dest:
            messagebox.showerror(APP_TITLE, "Defina a pasta de destino.")
            return
        try:
            roots_p = normalize_roots(roots)
        except FileNotFoundError as e:
            messagebox.showerror(APP_TITLE, str(e))
            return
        exts = parse_exts(self.var_exts.get())
        if not exts:
            messagebox.showerror(APP_TITLE, "Informe ao menos uma extensão (ex.: .rar).")
            return
        dest_p = Path(dest).expanduser().resolve()
        try:
            dest_p.mkdir(parents=True, exist_ok=True)
        except OSError as e:
            messagebox.showerror(APP_TITLE, f"Erro no destino: {e}")
            return

        # Preparar estado
        self.stop_event.clear()
        self.btn_search.configure(state=tk.DISABLED)
        self.btn_cancel.configure(state=tk.NORMAL)
        self.pb.start(10)
        self.var_status.set("Buscando...")
        self.txt_log.delete('1.0', tk.END)
        self._clear_tree()
        self.found_hits.clear()
        self.btn_copy_sel.configure(state=tk.DISABLED)
        self.btn_copy_all.configure(state=tk.DISABLED)

        follow_symlinks = self.var_symlinks.get()

        # Disparar thread de busca
        def worker():
            try:
                find_files(
                    roots=roots_p,
                    exts=exts,
                    follow_symlinks=follow_symlinks,
                    stop_event=self.stop_event,
                    log_q=self.log_q,
                    hit_q=self.hit_q,
                )
            except Exception as e:
                self.log_q.put(f"[FATAL] {e}")
            finally:
                self.log_q.put("__SEARCH_END__")

        self.worker_thread = threading.Thread(target=worker, daemon=True)
        self.worker_thread.start()

    def cancel(self):
        if (self.worker_thread and self.worker_thread.is_alive()) or (self.copy_thread and self.copy_thread.is_alive()):
            self.stop_event.set()
            self.var_status.set("Cancelando...")
        else:
            self.var_status.set("Pronto.")

    def _clear_tree(self):
        for iid in self.tree.get_children():
            self.tree.delete(iid)

    def _add_hit_to_tree(self, hit: SearchHit):
        src, root, size_b, mtime_ts = hit
        name = src.name
        rel = _relative_or_anchor(src, root)
        size_mb = f"{size_b/1024/1024:.2f}"
        mod = datetime.fromtimestamp(mtime_ts).strftime('%Y-%m-%d %H:%M:%S')
        iid = self.tree.insert("", tk.END, values=(name, str(root), str(rel), size_mb, mod))
        # Guardar índice alinhado
        self.tree.set(iid, "__idx__", str(len(self.found_hits)))

    def _poll_queues(self):
        # Log
        try:
            while True:
                msg = self.log_q.get_nowait()
                if msg == "__SEARCH_END__":
                    # finalizar UI de busca
                    self.pb.stop()
                    self.btn_search.configure(state=tk.NORMAL)
                    self.btn_cancel.configure(state=tk.DISABLED)
                    self.var_status.set("Busca concluída.")
                    # Habilitar ações de cópia se houver resultados
                    if self.found_hits:
                        self.btn_copy_sel.configure(state=tk.NORMAL)
                        self.btn_copy_all.configure(state=tk.NORMAL)
                    continue
                if msg == "__COPY_END__":
                    self.pb.stop()
                    self.btn_search.configure(state=tk.NORMAL)
                    self.btn_cancel.configure(state=tk.DISABLED)
                    if self.stop_event.is_set():
                        self.var_status.set("Cópia cancelada.")
                    else:
                        self.var_status.set("Cópia concluída.")
                    continue
                self.txt_log.insert(tk.END, msg + "\n")
                self.txt_log.see(tk.END)
        except queue.Empty:
            pass

        # Hits
        try:
            while True:
                hit = self.hit_q.get_nowait()
                self.found_hits.append(hit)
                self._add_hit_to_tree(hit)
        except queue.Empty:
            pass

        self.after(100, self._poll_queues)

    # ----- Seleção helpers -----
    def select_all(self):
        for iid in self.tree.get_children():
            self.tree.selection_add(iid)

    def clear_selection(self):
        self.tree.selection_remove(self.tree.selection())

    def invert_selection(self):
        current = set(self.tree.selection())
        all_items = set(self.tree.get_children())
        self.clear_selection()
        for iid in (all_items - current):
            self.tree.selection_add(iid)

    def _get_selected_hits(self) -> List[SearchHit]:
        hits: List[SearchHit] = []
        for iid in self.tree.selection():
            # recuperar pelo texto exibido
            vals = self.tree.item(iid, 'values')
            # reconstruir hit pela combinação (root + relpath), que é única
            root = Path(vals[1])
            rel = Path(vals[2])
            src = root.joinpath(rel)
            # procurar na lista (linear é suficiente aqui)
            for h in self.found_hits:
                if h[0] == src:
                    hits.append(h)
                    break
        return hits

    # ----- Execução: Cópia -----
    def _do_copy(self, hits: List[SearchHit]):
        dest = Path(self.var_dest.get().strip()).expanduser().resolve()
        overwrite = self.var_overwrite.get()
        dry_run = self.var_dryrun.get()

        copied = 0
        total = len(hits)
        for (src, root, _sz, _mt) in hits:
            if self.stop_event.is_set():
                break
            try:
                msg = copy_with_structure(src, root, dest, overwrite=overwrite, dry_run=dry_run)
                if msg:
                    self.log_q.put(msg)
                if msg and msg.startswith('[OK'):
                    copied += 1
            except PermissionError:
                self.log_q.put(f"[ERRO - permissão] {src}")
            except OSError as e:
                self.log_q.put(f"[ERRO - OS] {src}: {e}")
        self.log_q.put(f"Cópia finalizada. Selecionados: {total}; Copiados/Simulados: {copied}")

    def copy_selected(self):
        hits = self._get_selected_hits()
        if not hits:
            messagebox.showinfo(APP_TITLE, "Nenhum item selecionado.")
            return
        self._start_copy(hits)

    def copy_all(self):
        if not self.found_hits:
            messagebox.showinfo(APP_TITLE, "Nenhum resultado para copiar.")
            return
        self._start_copy(self.found_hits[:])

    def _start_copy(self, hits: List[SearchHit]):
        if self.copy_thread and self.copy_thread.is_alive():
            messagebox.showwarning(APP_TITLE, "Já existe uma cópia em andamento.")
            return
        self.stop_event.clear()
        self.btn_search.configure(state=tk.DISABLED)
        self.btn_cancel.configure(state=tk.NORMAL)
        self.pb.start(10)
        self.var_status.set("Copiando...")

        def worker():
            try:
                self._do_copy(hits)
            except Exception as e:
                self.log_q.put(f"[FATAL] {e}")
            finally:
                self.log_q.put("__COPY_END__")

        self.copy_thread = threading.Thread(target=worker, daemon=True)
        self.copy_thread.start()


def _relative_or_anchor(src: Path, root: Path) -> Path:
    try:
        return src.relative_to(root)
    except Exception:
        return src.relative_to(src.anchor)


def main():
    app = App()
    app.mainloop()


if __name__ == '__main__':
    main()
