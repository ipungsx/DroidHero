if(NOT TARGET game-activity::game-activity)
add_library(game-activity::game-activity STATIC IMPORTED)
set_target_properties(game-activity::game-activity PROPERTIES
    IMPORTED_LOCATION "/root/.gradle/caches/9.3.1/transforms/6b0cdf291b09a1ffa82b6ed7b8c3bded/transformed/jetified-games-activity-4.4.2/prefab/modules/game-activity/libs/android.armeabi-v7a/libgame-activity.a"
    INTERFACE_INCLUDE_DIRECTORIES "/root/.gradle/caches/9.3.1/transforms/6b0cdf291b09a1ffa82b6ed7b8c3bded/transformed/jetified-games-activity-4.4.2/prefab/modules/game-activity/include"
    INTERFACE_LINK_LIBRARIES ""
)
endif()

if(NOT TARGET game-activity::game-activity_static)
add_library(game-activity::game-activity_static STATIC IMPORTED)
set_target_properties(game-activity::game-activity_static PROPERTIES
    IMPORTED_LOCATION "/root/.gradle/caches/9.3.1/transforms/6b0cdf291b09a1ffa82b6ed7b8c3bded/transformed/jetified-games-activity-4.4.2/prefab/modules/game-activity_static/libs/android.armeabi-v7a/libgame-activity_static.a"
    INTERFACE_INCLUDE_DIRECTORIES "/root/.gradle/caches/9.3.1/transforms/6b0cdf291b09a1ffa82b6ed7b8c3bded/transformed/jetified-games-activity-4.4.2/prefab/modules/game-activity_static/include"
    INTERFACE_LINK_LIBRARIES ""
)
endif()

