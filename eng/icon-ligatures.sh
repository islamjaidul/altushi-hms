#!/bin/sh
# The single list of icon ligatures the product renders — shared by the guard that checks the font
# contains them and the script that builds the font. Two extractors that could disagree would put us
# straight back where we started.
#
# Two sources, because icons reach a page two ways:
#   <span class="icon">badge</span>          — literal, in a Razor view
#   new(..., "badge", Group)                 — an argument to a NavItem
set -eu
root="${1:-src}"

{
  # A literal ligature inside an element carrying the .icon class. Values containing @ are computed
  # and cannot be resolved here.
  grep -rhoE 'class="icon[a-z0-9 -]*"[^>]*>[a-z_0-9]+' "$root" --include='*.cshtml' \
    | sed -E 's/.*>//'

  # Nav registries: NavItem(module, title, url, permission, icon, group).
  grep -rhoE 'new\("[^"]*", *"[^"]*", *"[^"]*", *[^,]+, *"[a-z_0-9]+"' "$root" --include='*.cs' \
    | grep -oE '"[a-z_0-9]+"$' | tr -d '"'

  # Toast and empty-state icons passed from page models.
  grep -rhoE 'Toast\([^)]*, *"[a-z_0-9]+"\)' "$root" --include='*.cs' \
    | grep -oE '"[a-z_0-9]+"\)$' | tr -d '")'
} | grep -vE '^$' | sort -u
