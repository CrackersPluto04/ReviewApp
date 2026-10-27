package com.reviewapp.android.ui.navigation

import android.content.Intent
import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxWidth
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
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.core.util.Consumer
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavController
import androidx.navigation.NavDestination
import androidx.navigation.NavDestination.Companion.hasRoute
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navDeepLink
import androidx.navigation.navOptions
import com.reviewapp.android.domain.model.Media
import com.reviewapp.android.domain.model.MediaType
import com.reviewapp.android.ui.achievements.UnlockBannerHost
import com.reviewapp.android.ui.auth.LoginScreen
import com.reviewapp.android.ui.collection.CollectionActionsHost
import com.reviewapp.android.ui.collection.CollectionScreen
import com.reviewapp.android.ui.discover.DiscoverMusicScreen
import com.reviewapp.android.ui.discover.DiscoverScreen
import com.reviewapp.android.ui.home.HomeScreen
import com.reviewapp.android.ui.media.MediaDetailsScreen
import com.reviewapp.android.ui.notifications.NotificationBadgeViewModel
import com.reviewapp.android.ui.notifications.NotificationPollingEffect
import com.reviewapp.android.ui.notifications.NotificationTarget
import com.reviewapp.android.ui.notifications.NotificationsScreen
import com.reviewapp.android.ui.profile.FollowListScreen
import com.reviewapp.android.ui.profile.ProfileScreen
import com.reviewapp.android.ui.profile.ProfileTab
import com.reviewapp.android.ui.review.ReviewScreen
import com.reviewapp.android.ui.review.WriteReviewScreen
import com.reviewapp.android.ui.search.SearchScreen
import com.reviewapp.android.ui.settings.AccountSettingsScreen
import com.reviewapp.android.ui.subscription.PremiumRequiredHost
import com.reviewapp.android.ui.subscription.PremiumScreen
import com.reviewapp.android.ui.subscription.SubscriptionReturnScreen
import com.reviewapp.android.ui.usersearch.UserSearchScreen
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
    Profile("Profile", MyProfileRoute, MyProfileRoute::class, Icons.Filled.Person, Icons.Outlined.Person),
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
    // Errors of the favourite / add-to-collection buttons, which live on many screens
    val snackbarHostState = remember { SnackbarHostState() }
    val badgeViewModel: NotificationBadgeViewModel = viewModel(factory = NotificationBadgeViewModel.Factory)
    val unseenNotifications by badgeViewModel.unseenCount.collectAsStateWithLifecycle()
    NotificationPollingEffect(badgeViewModel, isLoggedIn = unseenNotifications != null)
    NewIntentDeepLinks(navController)

    Scaffold(
        // Every screen has its own Scaffold for its top bar, this one only adds the bottom bar
        contentWindowInsets = WindowInsets(0),
        snackbarHost = { SnackbarHost(snackbarHostState) },
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
                            icon = {
                                // The bell is on the Home screen, so its tab shows that there is something new
                                val showDot = tab == TopLevelTab.Home && (unseenNotifications ?: 0) > 0
                                BadgedBox(badge = { if (showDot) Badge() }) {
                                    Icon(if (selected) tab.selectedIcon else tab.icon, contentDescription = null)
                                }
                            },
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
        CollectionActionsHost(snackbarHostState) {
            AppNavHost(
                navController,
                unseenNotifications,
                // Consuming the bottom bar's padding stops the inner Scaffolds adding the system bar inset again
                Modifier.padding(padding).consumeWindowInsets(padding),
            )
        }
        // Over every screen, unlocks can come from any request
        Box(Modifier.fillMaxWidth()) {
            UnlockBannerHost(
                onOpenAchievements = { username ->
                    navController.navigate(ProfileRoute(username, ProfileTab.Achievements.name))
                },
                modifier = Modifier.align(Alignment.TopCenter),
            )
        }
        // Any request can hit a Premium feature (402)
        PremiumRequiredHost(onSeePremium = { navController.navigate(PremiumRoute) { launchSingleTop = true } })
    }
}

/**
 * Deep links that arrive while the app is running (the activity is `singleTask`), e.g. coming back from
 * checkout in the browser. `handleDeepLink` would restart the whole task for these NEW_TASK intents, so
 * the link is navigated to on top of the current screen instead. Cold starts go through the graph's deep links.
 */
@Composable
private fun NewIntentDeepLinks(navController: NavHostController) {
    val activity = LocalContext.current as? ComponentActivity ?: return
    DisposableEffect(activity, navController) {
        val listener = Consumer<Intent> { intent ->
            val uri = intent.data ?: return@Consumer
            // An earlier return screen is replaced, not reused: its ViewModel holds the old result
            runCatching { navController.navigate(uri, navOptions { popUpTo<SubscriptionReturnRoute> { inclusive = true } }) }
        }
        activity.addOnNewIntentListener(listener)
        onDispose { activity.removeOnNewIntentListener(listener) }
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
private fun AppNavHost(navController: NavHostController, unseenNotifications: Int?, modifier: Modifier) {
    val openMedia: (Media) -> Unit = { media ->
        navController.navigate(MediaDetailsRoute(media.mediaType.id, media.externalApiId))
    }
    val goToLogin: () -> Unit = { navController.navigate(LoginRoute) { launchSingleTop = true } }
    val openProfile: (String) -> Unit = { username -> navController.navigate(ProfileRoute(username)) }
    val goBack: () -> Unit = { navController.popBackStack() }
    val openMediaById: (MediaType, String) -> Unit = { type, id -> navController.navigate(MediaDetailsRoute(type.id, id)) }
    val editReview: (MediaType, String) -> Unit = { type, id -> navController.navigate(WriteReviewRoute(type.id, id)) }
    val openReview: (Int) -> Unit = { id -> navController.navigate(ReviewRoute(id)) }
    val openPremium: () -> Unit = { navController.navigate(PremiumRoute) { launchSingleTop = true } }

    // A profile is the Profile tab (no username, no back button) or any user's page
    @Composable
    fun Profile(onBack: (() -> Unit)?) = ProfileScreen(
        onBack = onBack,
        onLogin = goToLogin,
        onOpenMedia = openMediaById,
        onEditReview = editReview,
        onOpenReview = openReview,
        onOpenFollowList = { username, following -> navController.navigate(FollowListRoute(username, following)) },
        onOpenCollection = { id -> navController.navigate(CollectionRoute(id)) },
        onOpenSettings = { navController.navigate(AccountSettingsRoute) { launchSingleTop = true } },
    )

    NavHost(navController = navController, startDestination = HomeRoute, modifier = modifier) {
        composable<HomeRoute> {
            HomeScreen(
                onSearch = { query -> navController.navigate(SearchRoute(query)) },
                onOpenMedia = openMedia,
                onOpenReview = { review -> openReview(review.id) },
                onOpenReviewMedia = { review -> openMediaById(review.mediaType, review.externalApiId) },
                onOpenProfile = openProfile,
                onOpenUserSearch = { navController.navigate(UserSearchRoute) },
                unseenNotifications = unseenNotifications,
                onOpenNotifications = { navController.navigate(NotificationsRoute) { launchSingleTop = true } },
            )
        }
        composable<DiscoverMoviesRoute> { DiscoverScreen(MediaType.Movie, onOpenMedia = openMedia) }
        composable<DiscoverSeriesRoute> { DiscoverScreen(MediaType.Series, onOpenMedia = openMedia) }
        composable<DiscoverMusicRoute> { DiscoverMusicScreen(onOpenMedia = openMedia) }
        composable<MyProfileRoute> { Profile(onBack = null) }

        composable<SearchRoute> {
            SearchScreen(onBack = goBack, onOpenMedia = openMedia)
        }
        composable<MediaDetailsRoute> {
            MediaDetailsScreen(
                onBack = goBack,
                onWriteReview = editReview,
                onLoginRequired = goToLogin,
                onOpenProfile = openProfile,
                onOpenReview = openReview,
            )
        }
        composable<ReviewRoute>(
            // e.g. adb shell am start -d "reviewapp://review/12?replyId=34"
            deepLinks = listOf(navDeepLink<ReviewRoute>(basePath = "reviewapp://review")),
        ) {
            ReviewScreen(
                onBack = goBack,
                onLogin = goToLogin,
                onOpenProfile = openProfile,
                onOpenMedia = openMediaById,
                onEditReview = editReview,
            )
        }
        composable<ProfileRoute> { Profile(onBack = goBack) }
        composable<FollowListRoute> {
            FollowListScreen(onBack = goBack, onOpenProfile = openProfile, onLoginRequired = goToLogin)
        }
        composable<CollectionRoute> {
            CollectionScreen(onBack = goBack, onOpenMedia = openMedia)
        }
        composable<NotificationsRoute> {
            NotificationsScreen(
                onBack = goBack,
                onLogin = goToLogin,
                onOpenTarget = { target ->
                    navController.navigate(
                        when (target) {
                            is NotificationTarget.Review -> ReviewRoute(target.reviewId, target.replyId)
                            is NotificationTarget.Profile -> ProfileRoute(target.username)
                            is NotificationTarget.Achievements -> ProfileRoute(target.username, ProfileTab.Achievements.name)
                            NotificationTarget.Subscription -> PremiumRoute
                        },
                    )
                },
            )
        }
        composable<AccountSettingsRoute> { AccountSettingsScreen(onBack = goBack, onOpenPremium = openPremium) }
        composable<PremiumRoute> { PremiumScreen(onBack = goBack, onLogin = goToLogin) }
        composable<SubscriptionReturnRoute>(
            // e.g. adb shell am start -d "reviewapp://subscription/success"
            deepLinks = listOf(navDeepLink<SubscriptionReturnRoute>(basePath = "reviewapp://subscription")),
        ) {
            SubscriptionReturnScreen(
                onOpenPremium = {
                    navController.navigate(PremiumRoute) {
                        popUpTo<SubscriptionReturnRoute> { inclusive = true }
                        launchSingleTop = true
                    }
                },
                onHome = {
                    // Otherwise the tab's saved back stack would reopen on this screen later
                    navController.popBackStack<SubscriptionReturnRoute>(inclusive = true)
                    navController.navigateToTab(TopLevelTab.Home, reselect = false)
                },
                onLogin = goToLogin,
            )
        }
        composable<UserSearchRoute> {
            UserSearchScreen(onBack = goBack, onOpenProfile = openProfile)
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
