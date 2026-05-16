#pragma once

#include <Preferences.h>

#include "AppSettings.h"

class SettingsStore {
public:
  AppSettings load();
  void save(AppSettings& settings);
  AppSettings resetAll();

private:
  static void normalize(AppSettings& settings);
};