package com.reviewapp.android.ui.navigation

import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.LibraryMusic
import androidx.compose.material.icons.filled.Movie
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Tv
import androidx.compose.material.icons.outlined.Home
import androidx.compose.material.icons.outlined.LibraryMusic
import androidx.compose.material.icons.outlined.Movie
import androidx.compose.material.icons.outlined.Person
import androidx.compose.material.icons.outlined.Tv
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.navigation.NavController
import androidx.navigation.NavDestination
import androidx.navigation.NavDestination.Companion.hasRoute
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.ui.account.AccountScreen
import com.reviewapp.android.ui.auth.LoginScreen
import com.reviewapp.android.ui.discover.DiscoverMusicScreen
import com.reviewapp.android.ui.discover.DiscoverScreen
import com.reviewapp.android.ui.home.HomeScreen
import com.reviewapp.android.ui.media.MediaDetailsScreen
import com.reviewapp.android.ui.review.WriteReviewScreen
import com.reviewapp.android.ui.search.SearchScreen
import kotlin.reflect.KClass

/** The bottom navigation tabs, each keeps its own back stack (saved and restored on switch). */
enum class TopLevelTab(
    val label: String,
    val route: Any,
    val routeClass: KClass<*>,
    val selectedIcon: ImageVector,
    val icon: ImageVector,
) {
    Home("Home", HomeRoute, HomeRoute::class, Icons.Filled.Home, Icons.Outlined.Home),
    Movies("Movies", DiscoverMoviesRoute, DiscoverMoviesRoute::class, Icons.Filled.Movie, Icons.Outlined.Movie),
    Series("Series", DiscoverSeriesRoute, DiscoverSeriesRoute::class, Icons.Filled.Tv, Icons.Outlined.Tv),
    Music("Music", DiscoverMusicRoute, DiscoverMusicRoute::class, Icons.Filled.LibraryMusic, Icons.Outlined.LibraryMusic),
    Profile("Profile", AccountRoute, AccountRoute::class, Icons.Filled.Person, Icons.Outlined.Person),
}

private fun NavDestination.topLevelTab(): TopLevelTab? =
    TopLevelTab.entries.firstOrNull { hasRoute(it.routeClass) }

@Composable
fun ReviewAppRoot() {
    val navController = rememberNavController()
    val backStackEntry by navController.currentBackStackEntryAsState()
    val destination = backStackEntry?.destination
    // The tab stays highlighted on the screens opened from it (details, search, ...)
    var selectedTab by rememberSaveable { mutableStateOf(TopLevelTab.Home) }

    DisposableEffect(navController) {
        val listener = NavController.OnDestinationChangedListener { _, dest, _ ->
            dest.topLevelTab()?.let { selectedTab = it }
        }
        navController.addOnDestinationChangedListener(listener)
        onDispose { navController.removeOnDestinationChangedListener(listener) }
    }

    val showBottomBar = destination?.hasRoute(LoginRoute::class) != true

    Scaffold(
        // Every screen has its own Scaffold for its top bar, this one only adds the bottom bar
        contentWindowInsets = WindowInsets(0),
        bottomBar = {
            if (showBottomBar) {
                NavigationBar {
                    TopLevelTab.entries.forEach { tab ->
                        val selected = tab == selectedTab
                        NavigationBarItem(
                            selected = selected,
                            onClick = {
                                // Set here too: a restored tab can open on a nested screen (e.g. details)
                                selectedTab = tab
                                navController.navigateToTab(tab, reselect = selected)
                            },
                            icon = { Icon(if (selected) tab.selectedIcon else tab.icon, contentDescription = null) },
                            label = { Text(tab.label) },
                            colors = NavigationBarItemDefaults.colors(
                                selectedIconColor = MaterialTheme.colorScheme.onPrimaryContainer,
                                selectedTextColor = MaterialTheme.colorScheme.primary,
                                indicatorColor = MaterialTheme.colorScheme.primaryContainer,
                            ),
                        )
                    }
                }
            }
        },
    ) { padding ->
        AppNavHost(
            navController,
            // Consuming the bottom bar's padding stops the inner Scaffolds adding the system bar inset again
            Modifier.padding(padding).consumeWindowInsets(padding),
        )
    }
}

private fun NavHostController.navigateToTab(tab: TopLevelTab, reselect: Boolean) {
    // Tapping the current tab again goes back to its first screen
    if (reselect && popBackStack(tab.route, inclusive = false)) return
    navigate(tab.route) {
        popUpTo(graph.findStartDestination().id) { saveState = true }
        launchSingleTop = true
        restoreState = true
    }
}

@Composable
private fun AppNavHost(navController: NavHostController, modifier: Modifier) {
    val openMedia: (Media) -> Unit = { media ->
        navController.navigate(MediaDetailsRoute(media.mediaType.id, media.externalApiId))
    }
    val goToLogin: () -> Unit = { navController.navigate(LoginRoute) { launchSingleTop = true } }

    NavHost(navController = navController, startDestination = HomeRoute, modifier = modifier) {
        composable<HomeRoute> {
            HomeScreen(
                onSearch = { query -> navController.navigate(SearchRoute(query)) },
                onOpenMedia = openMedia,
                onOpenReview = { review ->
                    navController.navigate(MediaDetailsRoute(review.mediaType.id, review.externalApiId))
                },
            )
        }
        composable<DiscoverMoviesRoute> { DiscoverScreen(MediaType.Movie, onOpenMedia = openMedia) }
        composable<DiscoverSeriesRoute> { DiscoverScreen(MediaType.Series, onOpenMedia = openMedia) }
        composable<DiscoverMusicRoute> { DiscoverMusicScreen(onOpenMedia = openMedia) }
        composable<AccountRoute> { AccountScreen(onLogin = goToLogin) }

        composable<SearchRoute> {
            SearchScreen(onBack = { navController.popBackStack() }, onOpenMedia = openMedia)
        }
        composable<MediaDetailsRoute> {
            MediaDetailsScreen(
                onBack = { navController.popBackStack() },
                onWriteReview = { type, id -> navController.navigate(WriteReviewRoute(type.id, id)) },
                onLoginRequired = goToLogin,
            )
        }
        composable<WriteReviewRoute> {
            WriteReviewScreen(onClose = { navController.popBackStack() })
        }
        composable<LoginRoute> {
            // Login opens on top of the screen that needed it and returns there afterwards
            LoginScreen(
                onLoggedIn = { navController.popBackStack() },
                onBack = { navController.popBackStack() },
            )
        }
    }
}
