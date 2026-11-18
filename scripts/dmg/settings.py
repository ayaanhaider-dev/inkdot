# dmgbuild settings for the InkDot installer window. Values come in through -D on the command line.
import os.path

app = defines["app"]           # path to InkDot.app
background = defines["background"]

format = "UDZO"
filesystem = "HFS+"
files = [app]
symlinks = {"Applications": "/Applications"}
icon_locations = {os.path.basename(app): (170, 205), "Applications": (470, 205)}

background = background
window_rect = ((200, 160), (640, 448))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
icon_size = 112
text_size = 13
label_pos = "bottom"
