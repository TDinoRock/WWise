#!/usr/bin/perl
# Generates the arena's placeholder audio and Wwise work units for FOUR audio outputs.
#
#   perl Tools/author_arena_content.pl "<path to WWise_WwiseProject>"
#
# Writes:
#   Originals/SFX/Arena/*.wav                    synthesised placeholder sources
#   Devices|Busses|Switches|Containers|Events/Default Work Unit.wwu
#   ../Assets/ArenaScaffold/WwiseIds.txt         GUID manifest consumed by Unity
#
# Every GUID below is fixed, so re-running is idempotent: existing Wwise objects keep
# their identity. The device and bus GUIDs are the ones authored by hand in Wwise.
use strict;
use warnings;
use File::Path qw(make_path);

my $proj = shift or die "usage: author_arena_content.pl <path to WWise_WwiseProject>\n";
my $PI = 3.14159265358979;
my $SR = 44100;

# ------------------------------------------------------------------ WAV synthesis

sub write_wav {
    my ($path, $samples) = @_;
    my $data = '';
    for my $s (@$samples) {
        my $v = $s; $v = 1 if $v > 1; $v = -1 if $v < -1;
        $data .= pack('s<', int($v * 32767));
    }
    open(my $fh, '>:raw', $path) or die "cannot write $path: $!";
    print $fh 'RIFF', pack('V', 36 + length $data), 'WAVE';
    print $fh 'fmt ', pack('V v v V V v v', 16, 1, 1, $SR, $SR * 2, 2, 16);
    print $fh 'data', pack('V', length $data), $data;
    close $fh;
}

my $seed = 12345;
sub rnd { $seed = ($seed * 1103515245 + 12345) & 0x7fffffff; return ($seed / 0x7fffffff) * 2 - 1; }

sub footstep {
    my @s; my $lp = 0; my $n = int($SR * 0.12);
    for my $i (0..$n-1) {
        my $t = $i / $SR;
        my $env = exp(-$t / 0.022);
        $lp += 0.25 * (rnd() - $lp);                       # one-pole low-pass on noise
        my $thump = sin(2*$PI*85*$t) * exp(-$t / 0.03);
        push @s, 0.9 * ($lp * 1.6 * $env + 0.5 * $thump);
    }
    return \@s;
}
sub bump {
    my @s; my $n = int($SR * 0.22);
    for my $i (0..$n-1) {
        my $t = $i / $SR;
        my $f = 110 * exp(-$t / 0.08) + 45;                # pitch drops fast
        my $body = sin(2*$PI*$f*$t) * exp(-$t / 0.07);
        my $click = ($t < 0.006) ? rnd() * (1 - $t/0.006) : 0;
        push @s, 0.95 * (0.9 * $body + 0.4 * $click);
    }
    return \@s;
}
sub turn {
    my @s; my $n = int($SR * 0.07);
    for my $i (0..$n-1) {
        my $t = $i / $SR;
        push @s, 0.6 * sin(2*$PI*(900 + 600*$t/0.07)*$t) * exp(-$t / 0.018);
    }
    return \@s;
}
sub drip {
    my @s; my $n = int($SR * 0.28);
    for my $i (0..$n-1) {
        my $t = $i / $SR;
        my $f = 600 + 1400 * exp(-$t / 0.03);
        push @s, 0.7 * sin(2*$PI*$f*$t) * exp(-$t / 0.06);
    }
    return \@s;
}
sub creak {
    my @s; my $n = int($SR * 0.55); my $ph = 0;
    for my $i (0..$n-1) {
        my $t = $i / $SR;
        my $f = 90 + 60 * (1 - exp(-$t / 0.2)) + 8 * sin(2*$PI*23*$t);
        $ph += $f / $SR; $ph -= 1 if $ph >= 1;
        my $saw = 2 * $ph - 1;
        my $wob = 0.6 + 0.4 * sin(2*$PI*11*$t);
        my $env = ($t < 0.05 ? $t/0.05 : 1) * exp(-($t-0.05 > 0 ? $t-0.05 : 0) / 0.25);
        push @s, 0.45 * $saw * $wob * $env;
    }
    return \@s;
}
sub chirp {
    my @s; my $n = int($SR * 0.22);
    for my $i (0..$n-1) {
        my $t = $i / $SR;
        my $pulse = ($t < 0.08) ? $t : ($t > 0.12 && $t < 0.2 ? $t - 0.12 : -1);
        my $v = 0;
        if ($pulse >= 0) { my $f = 2000 + 1500 * $pulse / 0.08; $v = sin(2*$PI*$f*$t) * exp(-$pulse / 0.03); }
        push @s, 0.5 * $v;
    }
    return \@s;
}
sub hum {   # 2.0 s seamless loop: 55 Hz x 2 s = 110 whole cycles; 0.5 Hz LFO = 1 whole cycle
    my @s; my $n = $SR * 2;
    for my $i (0..$n-1) {
        my $t = $i / $SR;
        my $lfo = 0.75 + 0.25 * sin(2*$PI*0.5*$t);
        my $v = 0.6*sin(2*$PI*55*$t) + 0.3*sin(2*$PI*110*$t) + 0.12*sin(2*$PI*165*$t) + 0.05*sin(2*$PI*220*$t);
        push @s, 0.35 * $v * $lfo;
    }
    return \@s;
}

my $sfx = "$proj/Originals/SFX/Arena";
make_path($sfx);
write_wav("$sfx/Footstep.wav", footstep());
write_wav("$sfx/Bump.wav",     bump());
write_wav("$sfx/Turn.wav",     turn());
write_wav("$sfx/Drip.wav",     drip());
write_wav("$sfx/Creak.wav",    creak());
write_wav("$sfx/Chirp.wav",    chirp());
write_wav("$sfx/Hum.wav",      hum());
print "wrote 7 wavs to $sfx\n";

# ------------------------------------------------------------------ fixed identities

my $WU_DEVICES  = '{0629F2A1-AE24-403F-9488-1C56F217C4B1}';
my $WU_BUSSES   = '{8C6D4EBB-2748-4606-8D7E-58E95DB7A826}';
my $WU_SWITCHES = '{C47C8439-19A9-4256-B12A-CBC579F10B2B}';
my $WU_EVENTS   = '{87C5080A-4619-4F48-9883-399EE9DD06CE}';
my $WU_CONT     = '{289F1513-4054-40B1-9FF7-7FA978B3D73D}';
my $WU_CONV     = '{09F1D17F-F77E-477E-9D26-D1BB1A7B8378}';
my $CONVERSION  = '{6D1B890C-9826-4384-BF07-C15223E9FB56}';   # Default Conversion Settings

# The four outputs. Order matters: index 0 is the main output the sound engine
# creates at init; 1..3 are added at runtime with AK::SoundEngine::AddOutput.
# "Quadernary" keeps the spelling used by the hand-authored Wwise objects.
#
# Routing model: every sound's DRY path goes to a muted bus, and the audible signal
# reaches the outputs through GAME-DEFINED AUXILIARY SENDS. Each Players_/Arena_ bus
# has one AuxBus child ("<Group>_<Route>_Send"), so the game can send one sound to
# any combination of the four outputs, each at its own level, per game object.
my @ROUTES = (
    { name => 'Primary',    device => 'System',            device_id => '{1C23F569-8F5B-4E70-B5C8-D2049802C42E}',
      master => 'Main Audio Bus',       master_id => '{1514A4D8-1DA6-412A-A17E-75CA0C2149F3}',
      players_id => '{B1A0F1C3-4D5E-4F60-8172-93A4B5C6D701}', arena_id => '{B1A0F1C3-4D5E-4F60-8172-93A4B5C6D702}' },

    { name => 'Secondary',  device => 'System_Secondary',  device_id => '{7E1F3A20-5B6C-4D8E-9F01-2A3B4C5D6E70}',
      master => 'Secondary Audio Bus',  master_id => '{B2A0F1C3-4D5E-4F60-8172-93A4B5C6D7E8}',
      players_id => '{B2A0F1C3-4D5E-4F60-8172-93A4B5C6D701}', arena_id => '{B2A0F1C3-4D5E-4F60-8172-93A4B5C6D702}' },

    { name => 'Tertiary',   device => 'System_Tertiary',   device_id => '{30FFB12C-CC76-432C-949D-5B20CF91EFDD}',
      master => 'Tertiary Audio Bus',   master_id => '{D01FC66B-94A2-43A0-B860-6E4F9D844BCD}',
      players_id => '{0926B24B-DDF2-485E-9DA6-2D5965BA9127}', arena_id => '{2DF9FCAB-92EA-47F1-BB53-CBDE915987A5}' },

    { name => 'Quadernary', device => 'System_Quadernary', device_id => '{B8521CA2-ADDD-48F6-AF77-23B59989B703}',
      master => 'Quadernary Audio Bus', master_id => '{E66ACDB0-DDCC-4FBB-973B-356B1640A22C}',
      players_id => '{063D5EF6-4D87-422B-A5B2-390E75443E66}', arena_id => '{AAF6BEB2-60CA-4E56-8FBA-0EC3A138F704}' },
);
my @GROUPS = ('Players', 'Arena');

# The dry path of every sound. Muted twice over (bus and per-sound Output Bus Volume) so
# nothing ever reaches an output except through the aux sends.
my $DRY_BUS    = 'Dry (Muted)';
my $DRY_BUS_ID = '{D0000000-0000-4000-8000-00000000D0D0}';
my $MUTE_DB    = -96;   # Wwise's floor for bus and output-bus volume

# name, bus group, wav, loop, pitch rand (cents), volume rand (dB), volume (dB)
my @SOUNDS = (
    [ 'Footstep', 'Players', 'Footstep.wav', 0, 150, 2,  -6 ],
    [ 'Bump',     'Players', 'Bump.wav',     0,  60, 1,  -3 ],
    [ 'Turn',     'Players', 'Turn.wav',     0,  80, 1, -12 ],
    [ 'Drip',     'Arena',   'Drip.wav',     0, 300, 3,  -8 ],
    [ 'Creak',    'Arena',   'Creak.wav',    0, 200, 3,  -8 ],
    [ 'Chirp',    'Arena',   'Chirp.wav',    0, 400, 3, -12 ],
    [ 'Hum',      'Arena',   'Hum.wav',      1,   0, 0, -14 ],
);

sub guid { my ($block, $i, $k) = @_; return sprintf('{%08X-%04X-4%03X-8%03X-%012X}', $block, $i, $k, $i, $k * 7919 + $i); }
my $short = 700000000;
sub short_id { $short += 1013; return $short; }

# AuxBus identity per group/route: deterministic, so re-running keeps it stable.
sub aux_name { my ($group, $r) = @_; return "${group}_$r->{name}_Send"; }
sub aux_id   { my ($gi, $ri) = @_; return guid(0xA0000000, $gi + 1, $ri + 1); }

# ------------------------------------------------------------------ XML helpers

sub head { my ($id, $tag) = @_; return qq{<?xml version="1.0" encoding="utf-8"?>\n<WwiseDocument Type="WorkUnit" ID="$id" SchemaVersion="133">\n\t<$tag>\n\t\t<WorkUnit Name="Default Work Unit" ID="$id" PersistMode="Standalone">\n\t\t\t<ChildrenList>\n}; }
sub tail { my ($tag) = @_; return qq{\t\t\t</ChildrenList>\n\t\t</WorkUnit>\n\t</$tag>\n</WwiseDocument>\n}; }
sub empty_wu { my ($id, $tag) = @_; return qq{<?xml version="1.0" encoding="utf-8"?>\n<WwiseDocument Type="WorkUnit" ID="$id" SchemaVersion="133">\n\t<$tag>\n\t\t<WorkUnit Name="Default Work Unit" ID="$id" PersistMode="Standalone"/>\n\t</$tag>\n</WwiseDocument>\n}; }
sub save { my ($path, $xml) = @_; open(my $fh, '>:raw', $path) or die "cannot write $path: $!"; print $fh $xml; close $fh; print "wrote $path\n"; }
sub prop { my ($name, $type, $value, $ind) = @_; return qq{$ind<Property Name="$name" Type="$type" Value="$value"/>\n}; }
sub volume_prop {
    my ($name, $db, $ind) = @_;
    return qq{$ind<Property Name="$name" Type="Real64">\n$ind\t<ValueList>\n$ind\t\t<Value>$db</Value>\n$ind\t</ValueList>\n$ind</Property>\n};
}

# --- Devices -------------------------------------------------------
{
    my $x = head($WU_DEVICES, 'Devices');
    for my $r (@ROUTES) {
        $x .= qq{\t\t\t\t<AudioDevice Name="$r->{device}" ID="$r->{device_id}" PluginName="System" CompanyID="0" PluginID="174" PluginType="7"/>\n};
    }
    $x .= qq{\t\t\t\t<AudioDevice Name="No_Output" ID="{5FF64B43-DB39-411D-8CF9-7328C7C53DDE}" PluginName="No Output" CompanyID="0" PluginID="181" PluginType="7"/>\n};
    $x .= tail('Devices');
    save("$proj/Devices/Default Work Unit.wwu", $x);
}

# --- Busses --------------------------------------------------------
# One master bus per output, each carrying its own Audio Device ShareSet. Because every
# ShareSet has exactly one output, Wwise routes purely by bus and no listener juggling is
# needed. Under each Players_/Arena_ bus sits the AuxBus the game sends into.
{
    my $x = head($WU_BUSSES, 'Busses');
    for my $ri (0..$#ROUTES) {
        my $r = $ROUTES[$ri];
        $x .= qq{\t\t\t\t<Bus Name="$r->{master}" ID="$r->{master_id}">\n};
        $x .= qq{\t\t\t\t\t<ReferenceList>\n\t\t\t\t\t\t<Reference Name="AudioDevice" PluginName="System" CompanyID="0" PluginID="174" PluginType="7">\n};
        $x .= qq{\t\t\t\t\t\t\t<ObjectRef Name="$r->{device}" ID="$r->{device_id}" WorkUnitID="$WU_DEVICES"/>\n};
        $x .= qq{\t\t\t\t\t\t</Reference>\n\t\t\t\t\t</ReferenceList>\n};
        $x .= qq{\t\t\t\t\t<ChildrenList>\n};
        for my $gi (0..$#GROUPS) {
            my $g = $GROUPS[$gi];
            my $busId = $g eq 'Players' ? $r->{players_id} : $r->{arena_id};
            $x .= qq{\t\t\t\t\t\t<Bus Name="${g}_$r->{name}" ID="$busId">\n};
            $x .= qq{\t\t\t\t\t\t\t<ChildrenList>\n};
            $x .= qq{\t\t\t\t\t\t\t\t<AuxBus Name="} . aux_name($g, $r) . qq{" ID="} . aux_id($gi, $ri) . qq{"/>\n};
            $x .= qq{\t\t\t\t\t\t\t</ChildrenList>\n};
            $x .= qq{\t\t\t\t\t\t</Bus>\n};
        }
        if ($ri == 0) {
            # The muted dry bus lives under the main output so it always has a valid device.
            $x .= qq{\t\t\t\t\t\t<Bus Name="$DRY_BUS" ID="$DRY_BUS_ID">\n\t\t\t\t\t\t\t<PropertyList>\n};
            $x .= volume_prop('BusVolume', $MUTE_DB, "\t\t\t\t\t\t\t\t");
            $x .= qq{\t\t\t\t\t\t\t</PropertyList>\n\t\t\t\t\t\t</Bus>\n};
        }
        $x .= qq{\t\t\t\t\t</ChildrenList>\n\t\t\t\t</Bus>\n};
    }
    $x .= tail('Busses');
    save("$proj/Busses/Default Work Unit.wwu", $x);
}

# --- Switches ------------------------------------------------------
# The OutputRoute switch group is gone: routing is by aux send now, so a sound is no longer
# tied to one output. The work unit stays, empty, so the project structure is unchanged.
save("$proj/Switches/Default Work Unit.wwu", empty_wu($WU_SWITCHES, 'Switches'));

# --- Containers (Actor-Mixer Hierarchy) ----------------------------
# One plain Sound per effect. Each sound keeps the GUID its switch container had, so the
# Events that target it are unchanged.
my %SOUND_ID;
{
    my $x = head($WU_CONT, 'Containers');
    my $i = 0;
    my $P = "\t\t\t\t\t\t";   # property indent
    for my $s (@SOUNDS) {
        my ($name, $group, $wav, $loop, $pitchRand, $volRand, $vol) = @$s;
        $i++;
        my $sid   = guid(0xC0000000, $i, 0); $SOUND_ID{$name} = $sid;
        my $srcid = guid(0xC2000000, $i, 1);
        my $modP  = guid(0xC3000000, $i, 1);
        my $modV  = guid(0xC4000000, $i, 1);
        my $media = 900000000 + $i * 100 + 1;

        my $props = '';
        # Routing. OverrideOutput is set explicitly even at top level, so the dry route can
        # never silently fall back to an inherited bus.
        $props .= prop('OverrideOutput',       'bool', 'True', $P);
        $props .= prop('OverrideGameAuxSends', 'bool', 'True', $P);
        $props .= prop('UseGameAuxSends',      'bool', 'True', $P);
        $props .= volume_prop('OutputBusVolume', $MUTE_DB, $P);
        $props .= prop('IsLoopingEnabled', 'bool', 'True', $P) if $loop;
        if ($pitchRand > 0) {
            $props .= qq{$P<Property Name="Pitch" Type="int32">\n$P\t<ModifierList>\n$P\t\t<ModifierInfo>\n$P\t\t\t<Modifier Name="" ID="$modP">\n$P\t\t\t\t<PropertyList>\n$P\t\t\t\t\t<Property Name="Max" Type="Real64" Value="$pitchRand"/>\n$P\t\t\t\t\t<Property Name="Min" Type="Real64" Value="-$pitchRand"/>\n$P\t\t\t\t</PropertyList>\n$P\t\t\t</Modifier>\n$P\t\t</ModifierInfo>\n$P\t</ModifierList>\n$P</Property>\n};
        }
        $props .= qq{$P<Property Name="Volume" Type="Real64">\n$P\t<ValueList>\n$P\t\t<Value>$vol</Value>\n$P\t</ValueList>\n};
        if ($volRand > 0) {
            $props .= qq{$P\t<ModifierList>\n$P\t\t<ModifierInfo>\n$P\t\t\t<Modifier Name="" ID="$modV">\n$P\t\t\t\t<PropertyList>\n$P\t\t\t\t\t<Property Name="Max" Type="Real64" Value="$volRand"/>\n$P\t\t\t\t\t<Property Name="Min" Type="Real64" Value="-$volRand"/>\n$P\t\t\t\t</PropertyList>\n$P\t\t\t</Modifier>\n$P\t\t</ModifierInfo>\n$P\t</ModifierList>\n};
        }
        $props .= qq{$P</Property>\n};

        $x .= qq{\t\t\t\t<Sound Name="$name" ID="$sid" ShortID="} . short_id() . qq{">\n};
        $x .= qq{\t\t\t\t\t<PropertyList>\n$props\t\t\t\t\t</PropertyList>\n};
        $x .= qq{\t\t\t\t\t<ReferenceList>\n};
        $x .= qq{\t\t\t\t\t\t<Reference Name="Conversion" CompanyID="4095" PluginID="65535" PluginType="15">\n\t\t\t\t\t\t\t<ObjectRef Name="Default Conversion Settings" ID="$CONVERSION" WorkUnitID="$WU_CONV"/>\n\t\t\t\t\t\t</Reference>\n};
        $x .= qq{\t\t\t\t\t\t<Reference Name="OutputBus" CompanyID="4095" PluginID="65535" PluginType="15">\n\t\t\t\t\t\t\t<ObjectRef Name="$DRY_BUS" ID="$DRY_BUS_ID" WorkUnitID="$WU_BUSSES"/>\n\t\t\t\t\t\t</Reference>\n};
        $x .= qq{\t\t\t\t\t</ReferenceList>\n};
        $x .= qq{\t\t\t\t\t<ChildrenList>\n\t\t\t\t\t\t<AudioFileSource Name="$name" ID="$srcid">\n\t\t\t\t\t\t\t<Language>SFX</Language>\n\t\t\t\t\t\t\t<AudioFile>Arena\\$wav</AudioFile>\n\t\t\t\t\t\t\t<MediaIDList>\n\t\t\t\t\t\t\t\t<MediaID ID="$media"/>\n\t\t\t\t\t\t\t</MediaIDList>\n\t\t\t\t\t\t</AudioFileSource>\n\t\t\t\t\t</ChildrenList>\n};
        $x .= qq{\t\t\t\t\t<ActiveSourceList>\n\t\t\t\t\t\t<ActiveSource Name="$name" ID="$srcid" Platform="Linked"/>\n\t\t\t\t\t</ActiveSourceList>\n};
        $x .= qq{\t\t\t\t</Sound>\n};
    }
    $x .= tail('Containers');
    save("$proj/Containers/Default Work Unit.wwu", $x);
}

# --- Events --------------------------------------------------------
my @EVENTS;
{
    my $x = head($WU_EVENTS, 'Events');
    my $i = 0; my $actionShort = 100;
    my $event = sub {
        my ($evName, $type, $target) = @_;
        $i++;
        my $eid = guid(0xE0000000, $i, 0);
        my $aid = guid(0xE1000000, $i, 0);
        push @EVENTS, [ $evName, $eid ];
        $actionShort++;
        return qq{\t\t\t\t<Event Name="$evName" ID="$eid">\n\t\t\t\t\t<ChildrenList>\n\t\t\t\t\t\t<Action Name="" ID="$aid" ShortID="$actionShort">\n\t\t\t\t\t\t\t<PropertyList>\n\t\t\t\t\t\t\t\t<Property Name="ActionType" Type="int16" Value="$type"/>\n\t\t\t\t\t\t\t</PropertyList>\n\t\t\t\t\t\t\t<ReferenceList>\n\t\t\t\t\t\t\t\t<Reference Name="Target" CompanyID="4095" PluginID="65535" PluginType="15">\n\t\t\t\t\t\t\t\t\t<ObjectRef Name="$target" ID="$SOUND_ID{$target}" WorkUnitID="$WU_CONT"/>\n\t\t\t\t\t\t\t\t</Reference>\n\t\t\t\t\t\t\t</ReferenceList>\n\t\t\t\t\t\t</Action>\n\t\t\t\t\t</ChildrenList>\n\t\t\t\t</Event>\n};
    };
    $x .= $event->("Play_$_->[0]", 1, $_->[0]) for @SOUNDS;   # 1 = Play
    $x .= $event->('Stop_Hum', 2, 'Hum');                     # 2 = Stop
    $x .= tail('Events');
    save("$proj/Events/Default Work Unit.wwu", $x);
}

# --- Manifest for Unity --------------------------------------------
{
    open(my $fh, '>', "$proj/../Assets/ArenaScaffold/WwiseIds.txt") or die "cannot write manifest: $!";
    print $fh "# Generated by Tools/author_arena_content.pl - type<TAB>name<TAB>guid\n";
    print $fh "Event\t$_->[0]\t$_->[1]\n" for @EVENTS;
    for my $gi (0..$#GROUPS) {
        for my $ri (0..$#ROUTES) { print $fh "AuxBus\t" . aux_name($GROUPS[$gi], $ROUTES[$ri]) . "\t" . aux_id($gi, $ri) . "\n"; }
    }
    print $fh "Bus\t$_->{master}\t$_->{master_id}\n" for @ROUTES;
    print $fh "Bus\t$DRY_BUS\t$DRY_BUS_ID\n";
    print $fh "AudioDevice\t$_->{device}\t$_->{device_id}\n" for @ROUTES;
    close $fh;
    print "wrote WwiseIds.txt\n";
}
printf "done: %d outputs, %d sounds, %d aux sends, %d events\n",
    scalar(@ROUTES), scalar(@SOUNDS), scalar(@ROUTES) * scalar(@GROUPS), scalar(@EVENTS);
